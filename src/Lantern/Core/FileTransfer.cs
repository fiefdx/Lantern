using Lantern.Domain;
using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// The sending half of a file transfer (F8, T5.3): announce, then numbered chunks, then done.
/// <para>
/// The bytes are read as they are needed - <see cref="FrameCodec.ChunkAsync"/> streams the file in 8 KB
/// slices - because the point of a file transfer is that it works for a file bigger than the memory of the
/// machine sending it. Nothing here knows what a connection is: a client hands it one frame writer that
/// goes to its own socket, the host hands it one that goes to everybody else in the room, and the framing,
/// the counting and the cancelling are the same either way.
/// </para>
/// </summary>
public static class FileSending
{
    /// <summary>
    /// Streams <paramref name="path"/> behind <paramref name="job"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the file went whole; <see langword="false"/> when it was stopped part
    /// way, in which case FILE_CANCEL has been written so nobody is left waiting for bytes that are not
    /// coming. A stopped transfer is not an exception: the person on the other end pressed Cancel, which
    /// is a decision, not a fault.
    /// </returns>
    public static async Task<bool> StreamAsync(
        FileTransferJob job,
        string path,
        Func<Frame, Task> write,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(write);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(job.CancellationToken, cancel);

        // First the announcement, so the other side knows what is coming, how big it is and which line of
        // the transcript it belongs to. The bytes follow, and none of them carry a name: they are only
        // ever pieces of the thing this frame described.
        await write(Frame.FileMeta(job.MessageId, job.Id, job.RoomName, job.SenderNickname, job.FileName, job.SizeBytes))
            .ConfigureAwait(false);

        var stopped = false;

        try
        {
            await foreach (var (sequence, bytes) in FrameCodec.ChunkAsync(path, linked.Token).ConfigureAwait(false))
            {
                await write(Frame.FileChunk(job.Id, sequence, bytes)).ConfigureAwait(false);

                // Progress is counted as it leaves, not as it is read: the bar should not reach 100% while
                // the last megabyte is still sitting in a socket buffer.
                job.AddBytes(bytes.Length);
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }

        if (stopped)
        {
            // Said out loud, because the other side cannot tell "cancelled" from "still arriving", and
            // would otherwise keep a half-written file and a bar that has stopped moving. If the link is
            // gone there is nobody to tell, and the transfer is over either way.
            try
            {
                await write(Frame.FileCancel(job.Id)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Nothing to say it to.
            }

            return false;
        }

        await write(Frame.FileDone(job.Id)).ConfigureAwait(false);
        return true;
    }
}

/// <summary>
/// The receiving half of a file transfer (F8, T5.3), shared by a remote client and by the host's own
/// window so there is one answer to "what do I do with a chunk".
/// <para>
/// Bytes go straight to a spool file under the user's own app data rather than to memory: a 20 MB file
/// arriving while a person is still deciding where to put it must cost a disk write, not twenty megabytes
/// of RAM per person in the room. The spool is what <see cref="SaveAsync"/> moves to wherever they chose,
/// which is also what makes a decline cheap - the file was never anywhere else.
/// </para>
/// <para>
/// A file is only ever finished when the byte count matches what FILE_META promised. A truncated file
/// quietly saved is worse than no file at all, because it looks like it worked.
/// </para>
/// </summary>
internal sealed class FileReceiving
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Incoming> transfers = [];

    /// <summary>
    /// Distinguishes this receiving window from every other one on the same machine. The spool is named
    /// after the transfer, and two Lantern windows on one laptop both receiving the same file would
    /// otherwise be told - correctly - that somebody already owns the file they are both holding (F8: the
    /// host is a member of its own room, so this happens in ordinary use, not just in a test).
    /// </summary>
    private readonly Guid receiverId = Guid.NewGuid();
    private readonly string spoolDirectory;
    private readonly Action<FileTransferJob> offered;
    private readonly Action<FileTransferJob, string> saved;
    private readonly Action<FileTransferJob, string> failed;
    private readonly Action<Guid> cancelPeer;

    public FileReceiving(
        string spoolDirectory,
        Action<FileTransferJob> offered,
        Action<FileTransferJob, string> saved,
        Action<FileTransferJob, string> failed,
        Action<Guid> cancelPeer)
    {
        this.spoolDirectory = spoolDirectory;
        this.offered = offered;
        this.saved = saved;
        this.failed = failed;
        this.cancelPeer = cancelPeer;
    }

    /// <summary>FILE_META: a file has been offered. The person is asked; the bytes start arriving anyway.</summary>
    public void Announce(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var id = frame.Header.TransferId;

        if (id is null)
        {
            return;
        }

        var name = Path.GetFileName((frame.Header.FileName ?? string.Empty).Trim());

        if (name.Length == 0)
        {
            name = "file";
        }

        var size = frame.Header.SizeBytes ?? -1;
        var job = new FileTransferJob(name, Math.Max(0, size), id, frame.Header.Nickname ?? string.Empty, frame.Header.Room ?? string.Empty, frame.Header.Id);

        if (size < 0)
        {
            failed(job, $"{name} was announced without a size.");
            return;
        }

        if (size > LanternProtocol.MaxReceivedFileBytes)
        {
            // The sender can announce whatever it likes in one frame; without a ceiling that is an
            // invitation to fill somebody's disk.
            failed(job, $"{name} is {FileSize.Describe(size)}, which is more than Lantern accepts.");
            return;
        }

        lock (gate)
        {
            if (transfers.ContainsKey(id.Value))
            {
                // Two announcements of one transfer: the second says nothing new, and starting again would
                // throw away bytes that have already arrived.
                return;
            }

            var incoming = new Incoming(job, Path.Combine(spoolDirectory, $"{id:N}-{receiverId:N}.part"));
            transfers[id.Value] = incoming;

            // Declining has to have somewhere to arrive, and the only thing the window is given is the job.
            // The registration is dropped with the job rather than disposed here: a token source holds its
            // own registrations, so nothing outlives the transfer it belongs to.
            job.Cancel.Token.Register(() => Discard(id.Value, $"{name} was turned down."));
        }

        offered(job);
    }

    /// <summary>FILE_CHUNK: one slice of an offered file, appended to its spool in the order it arrived.</summary>
    public void Chunk(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var id = frame.Header.TransferId;

        if (id is null)
        {
            return;
        }

        Incoming? incoming;

        lock (gate)
        {
            incoming = transfers.GetValueOrDefault(id.Value);
        }

        if (incoming is null)
        {
            // A chunk with no announcement behind it. There is no honest place to put it: not knowing the
            // name, the size or the sender means it cannot become a file, and guessing would invent one.
            return;
        }

        lock (gate)
        {
            if (incoming.Failure is not null || incoming.Job.Cancel.IsCancellationRequested)
            {
                return;
            }

            if (frame.Header.Sequence != incoming.Next)
            {
                Fail(incoming, $"{incoming.Job.FileName} arrived out of order.");
                return;
            }

            incoming.Next++;
            incoming.Written += frame.Body.Length;

            if (incoming.Written > incoming.Job.SizeBytes)
            {
                Fail(incoming, $"{incoming.Job.FileName} sent more bytes than it promised.");
                return;
            }

            try
            {
                incoming.Append(frame.Body);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // A full disk, or a folder that stopped existing. The person needs to hear it from the
                // transfer, not discover an empty progress bar later.
                Fail(incoming, $"{incoming.Job.FileName} could not be written to this machine: {ex.Message}");
                return;
            }
        }

        // Outside the lock: progress goes to a window, and a window is the thing most likely to throw.
        incoming.Job.AddBytes(frame.Body.Length);
    }

    /// <summary>FILE_DONE: the last chunk has arrived, so the count is checked against the promise.</summary>
    public void Complete(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var id = frame.Header.TransferId;

        if (id is null)
        {
            return;
        }

        Incoming? incoming;

        lock (gate)
        {
            incoming = transfers.GetValueOrDefault(id.Value);

            if (incoming is null || incoming.Failure is not null)
            {
                return;
            }

            if (incoming.Written != incoming.Job.SizeBytes)
            {
                // The file would open as a broken thing and blame the person for choosing to keep it.
                Fail(incoming, $"{incoming.Job.FileName} stopped after {incoming.Written:N0} of {incoming.Job.SizeBytes:N0} bytes.");
                return;
            }

            try
            {
                incoming.CloseSpool();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail(incoming, $"{incoming.Job.FileName} could not be finished: {ex.Message}");
                return;
            }

            incoming.Complete = true;
            incoming.Finished.TrySetResult(true);

            // The bytes are all here, so the transfer part of the job is over even before a person has
            // said where to put the file. Saving is a separate thing from receiving.
            incoming.Job.Finish();
        }
    }

    /// <summary>
    /// FILE_CANCEL from the other side: whoever is sending has stopped, or whoever is receiving over there
    /// (another person in the room) has declined and the host is passing it on.
    /// </summary>
    public void CancelledByPeer(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var id = frame.Header.TransferId;

        if (id is null)
        {
            return;
        }

        lock (gate)
        {
            if (transfers.GetValueOrDefault(id.Value) is { } incoming)
            {
                Fail(incoming, $"{incoming.Job.FileName} was stopped by {WhoSentIt(frame, incoming)}.");
            }
        }
    }

    /// <summary>
    /// Moves an arrived file to the place the person chose. Waits for the bytes if they have not all come
    /// in yet, because the window asks where to put it before the transfer has finished and there is no
    /// sense in refusing the answer.
    /// </summary>
    public async Task SaveAsync(FileTransferJob job, string path, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Incoming incoming;

        lock (gate)
        {
            incoming = transfers.GetValueOrDefault(job.Id)
                ?? throw new InvalidOperationException($"{job.FileName} is not arriving any more.");
        }

        // The file is on this machine already, so waiting for the last chunks is a wait for bytes rather
        // than for a decision; Cancel on the job ends it, and the failure says why.
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(job.Cancel.Token, cancel);

        try
        {
            await completedAsync(incoming, pending.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (incoming.Failure is null && !job.Cancel.IsCancellationRequested)
        {
            // Whoever asked for the save changed their mind, which is theirs to do. A cancellation that
            // came from the transfer failing is not that, and reporting it as one would hide the reason
            // somebody stopped - which is the one thing the person asking can act on.
            throw;
        }

        lock (gate)
        {
            if (incoming.Failure is { } failure)
            {
                throw new InvalidOperationException(failure);
            }

            if (!incoming.Complete)
            {
                throw new InvalidOperationException($"{incoming.Job.FileName} did not finish arriving.");
            }
        }

        await Task.Run(() => Move(incoming, path, job), cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// Ends every unfinished transfer, which is what a lost connection means: nobody is going to send the
    /// rest, and a window left waiting on a transfer that can never finish is a window that hangs.
    /// </summary>
    public void AbandonUnfinished(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        List<Incoming> abandoned;

        lock (gate)
        {
            abandoned = transfers.Values.Where(t => t.Failure is null && !t.Complete).ToList();

            foreach (var incoming in abandoned)
            {
                Fail(incoming, reason);
            }
        }
    }

    /// <summary>Every transfer still on hand, oldest first - for a window that lists what is arriving.</summary>
    public IReadOnlyList<FileTransferJob> Active()
    {
        lock (gate)
        {
            return transfers.Values
                .Where(t => t.Failure is null && !t.Complete)
                .Select(t => t.Job)
                .ToArray();
        }
    }

    private async Task completedAsync(Incoming incoming, CancellationToken cancel)
    {
        var finished = await Task.WhenAny(incoming.Finished.Task, Task.Delay(Timeout.Infinite, cancel)).ConfigureAwait(false);

        if (finished != incoming.Finished.Task)
        {
            cancel.ThrowIfCancellationRequested();
        }
    }

    private void Move(Incoming incoming, string path, FileTransferJob job)
    {
        string moved;

        try
        {
            var folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            if (File.Exists(path))
            {
                throw new InvalidOperationException($"\"{Path.GetFileName(path)}\" is already there. Choose another name.");
            }

            // Move rather than copy: the spool has the whole file already, and a copy of a 20 MB file onto
            // itself would be 20 MB and several seconds of nothing.
            File.Move(incoming.SpoolPath, path);
            moved = path;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A read-only folder, a full disk, a name Windows refuses. The file is still in the spool, and
            // the status bar says where, so the choice can be made again.
            throw new InvalidOperationException($"{job.FileName} could not be saved there: {ex.Message}", ex);
        }

        lock (gate)
        {
            transfers.Remove(job.Id);
        }

        saved(job, moved);
    }

    private void Discard(Guid id, string reason)
    {
        Incoming? incoming;

        lock (gate)
        {
            incoming = transfers.GetValueOrDefault(id);

            if (incoming is null || incoming.Failure is not null || incoming.Complete)
            {
                return;
            }

            Fail(incoming, reason);
        }

        // Only as far as the link that carried it: the host passes it on to whoever is sending.
        cancelPeer(id);
    }

    /// <summary>Closes and deletes the spool and tells whoever was waiting. Call with <see cref="gate"/> held.</summary>
    private void Fail(Incoming incoming, string reason)
    {
        incoming.Failure = reason;
        incoming.DiscardSpool();
        incoming.Finished.TrySetResult(false);

        // A transfer that failed is a transfer that is over. A window waiting on the job - or showing its
        // progress - has to find out now rather than watching a number that will never move again.
        incoming.Job.CancelTransfer();
        incoming.Job.Finish();

        transfers.Remove(incoming.Job.Id);

        Dispatch.Safe(() => failed(incoming.Job, reason));
    }

    private static string WhoSentIt(Frame frame, Incoming incoming)
        => string.IsNullOrWhiteSpace(incoming.Job.SenderNickname) ? "the other side" : incoming.Job.SenderNickname;

    /// <summary>One file on its way in: the job the window watches, and the spool the bytes land in.</summary>
    private sealed class Incoming
    {
        private FileStream? spool;

        public Incoming(FileTransferJob job, string spoolPath)
        {
            Job = job;
            SpoolPath = spoolPath;
        }

        public FileTransferJob Job { get; }

        public string SpoolPath { get; }

        public int Next { get; set; }

        public long Written { get; set; }

        public bool Complete { get; set; }

        public string? Failure { get; set; }

        public TaskCompletionSource<bool> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Append(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return;
            }

            spool ??= Create();
            spool.Write(bytes);
        }

        public void CloseSpool()
        {
            spool?.Dispose();
            spool = null;
        }

        public void DiscardSpool()
        {
            CloseSpool();

            try
            {
                if (File.Exists(SpoolPath))
                {
                    File.Delete(SpoolPath);
                }
            }
            catch (IOException)
            {
                // A spool that will not delete is a stray file in the user's app data. Not worth losing a
                // connection over, and the next run does not care about it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private FileStream Create()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SpoolPath)!);

            // One writer at a time and no async: the thread that received a chunk is the thread that writes
            // it, which is what keeps two chunks from interleaving inside the file. The disk is the slow
            // part either way, and a queue in front of it would only add a place to get it wrong.
            //
            // CreateNew, because a spool that two transfers both think they own is neither of them. The only
            // file that can legitimately be in the way is one left behind by a run that died, and that one
            // is worth overwriting.
            return new FileStream(SpoolPath, new FileStreamOptions
            {
                Mode = File.Exists(SpoolPath) ? FileMode.Create : FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = FrameCodec.ChunkBytes,
                Options = FileOptions.None,
            });
        }
    }
}
