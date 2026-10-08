using System.Threading.Channels;
using Lantern.Domain;
using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// File transfers as the host sees them (F8, T5.3).
/// <para>
/// A file does not go from one window to another: it goes from the machine that has it through the host to
/// everybody else who wants it. That makes the host responsible for three things nobody else can do. It is
/// the only place that knows whether a person still wants a file (they may leave, decline, or stop reading);
/// it is the only place that can see whether the bytes arriving match the size promised; and it is the only
/// place where the speed of the slowest recipient can be measured against the speed of the sender.
/// </para>
/// <para>
/// So every transfer has a queue of its own and one pump. Whoever produces the bytes - a client's read loop,
/// or the host reading its own disk - writes into that queue and slows down when it is full, which pushes the
/// wait back to the machine sending the file instead of leaving it in the host's memory. The pump hands each
/// chunk to the people who still want it, waiting while any of them falls behind by more than
/// <see cref="LanternProtocol.FileQueueChunks"/> chunks. A file therefore costs the host about one chunk per
/// person in the room, whatever size the file is.
/// </para>
/// </summary>
public sealed partial class ChatServer
{
    /// <summary>How often the flow control looks to see whether a slow machine has caught up.</summary>
    private static readonly TimeSpan FlowPollInterval = TimeSpan.FromMilliseconds(5);

    /// <summary>
    /// Files crossing the host right now, by transfer id. Nothing here outlives the transfer: a completed,
    /// cancelled or abandoned file is forgotten, because a host that remembered them would be a host with a
    /// list of things it is no longer doing.
    /// </summary>
    private readonly Dictionary<Guid, Transfer> transfers = [];

    /// <summary>
    /// Sends a file the host itself chose into the room it is in (F8).
    /// <para>
    /// The job comes back immediately and the bytes go on travelling afterwards. A twenty megabyte file over
    /// Bluetooth takes minutes, and a window that waited on the call would be a window nobody could type in
    /// - so the caller watches <see cref="FileTransferJob.Fraction"/> and calls
    /// <see cref="FileTransferJob.CancelTransfer"/> if the person changes their mind.
    /// </para>
    /// </summary>
    public FileTransferJob SendFile(IParticipant sender, string path)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var room = RoomOf(sender) ?? throw new InvalidOperationException("Pick a room before sending a file.");
        var file = Path.GetFullPath(path);

        if (!File.Exists(file))
        {
            throw new FileNotFoundException("That file is not there any more.", file);
        }

        var job = new FileTransferJob(Path.GetFileName(file), new FileInfo(file).Length, senderNickname: sender.Nickname, roomName: room.Name);
        var transfer = NewTransfer(job.MessageId, job.Id, room.Name, job.FileName, sender.ConnectionId, job, new FileInfo(file).Length);

        _ = Task.Run(() => StreamLocalFileAsync(transfer, job, file));

        return job;
    }

    /// <summary>
    /// Stops one person's copy of a file (F8: Cancel, or Decline on the notice). The others still get it -
    /// one person saying no is not a vote that ends the transfer - but when it was the last copy anybody
    /// wanted, the sender is told so it stops reading its own disk for nothing.
    /// </summary>
    public void DeclineFile(Guid transferId, IParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        Transfer? transfer;

        lock (gate)
        {
            transfer = transfers.GetValueOrDefault(transferId);

            if (transfer is not null)
            {
                transfer.Declined.Add(participant.ConnectionId);
            }
        }

        if (transfer is not null && !transfer.Stopped && RecipientsFor(transfer).Count == 0)
        {
            StopTransfer(transfer, $"{participant.Nickname} did not want it and nobody else did either");
        }
    }

    /// <summary>Ends the transfers a member was carrying or receiving, called when they disappear (F12).</summary>
    private void ForgetTransfersOf(string connectionId)
    {
        List<Transfer> sending = [];
        List<Transfer> receiving = [];

        lock (gate)
        {
            foreach (var transfer in transfers.Values)
            {
                if (transfer.SenderConnectionId == connectionId)
                {
                    sending.Add(transfer);
                }
                else
                {
                    // Gone, so their half of the file is over. Whether anybody else still wants it is the
                    // same question Cancel asks.
                    transfer.Declined.Add(connectionId);
                    receiving.Add(transfer);
                }
            }
        }

        foreach (var transfer in sending)
        {
            StopTransfer(transfer, "the machine sending it disappeared");
        }

        foreach (var transfer in receiving)
        {
            if (!transfer.Stopped && RecipientsFor(transfer).Count == 0)
            {
                StopTransfer(transfer, "nobody was left to receive it");
            }
        }
    }

    // ------------------------------------------------------------- the two halves

    private async Task StreamLocalFileAsync(Transfer transfer, FileTransferJob job, string file)
    {
        try
        {
            // The host's own file, read on a thread-pool thread and handed to the pump one frame at a time.
            // `write` waits when the pump is waiting, so the disk is never read further ahead of the room
            // than the room can take.
            await FileSending.StreamAsync(job, file, frame => RelayAsync(transfer, frame), job.Cancel.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file went away, or was never readable. The person who chose it needs a sentence, not a
            // progress bar that quietly stopped.
            StopTransfer(transfer, $"it could not be read ({ex.Message})");
            return;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            StopTransfer(transfer, $"{job.SenderNickname} stopped it");
            return;
        }
        finally
        {
            // However it ended, the host is finished reading that file, and anyone waiting on the job
            // should stop waiting.
            job.Finish();
        }

        transfer.Relay.Writer.TryComplete();

        try
        {
            await (transfer.Pump ?? Task.CompletedTask).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ChannelClosedException or OperationCanceledException)
        {
            // The pump stopped for a reason it already reported.
        }

        lock (gate)
        {
            transfers.Remove(transfer.Id);
        }
    }

    private async Task ReceiveFileMetaAsync(Frame frame, ChatServerSession session)
    {
        var room = RoomOf(session);

        if (room is null)
        {
            session.Receive(Frame.Error("Join a room before sending a file."));
            return;
        }

        if (frame.Header.TransferId is not { } id)
        {
            session.Receive(Frame.Error("That file was announced without a transfer id."));
            return;
        }

        // A peer may announce whatever size it likes in one frame, and the price of believing it is paid in
        // somebody else's disk. The same ceiling a receiver applies is applied here, so the refusal is one
        // sentence to one person instead of a failed transfer in every window at once.
        var size = frame.Header.SizeBytes ?? -1;

        if (size < 0 || size > LanternProtocol.MaxReceivedFileBytes)
        {
            session.Receive(Frame.Error($"Lantern does not carry files of {size:N0} bytes."));
            return;
        }

        var name = Path.GetFileName((frame.Header.FileName ?? string.Empty).Trim());

        if (name.Length == 0)
        {
            session.Receive(Frame.Error("That file was announced without a name."));
            return;
        }

        lock (gate)
        {
            if (transfers.ContainsKey(id))
            {
                session.Receive(Frame.Error("That file is already on its way."));
                return;
            }
        }

        // The sender's own id for the transcript line is kept, so every window in the room shows this
        // file as the same event; only when it sent none does the host invent one.
        var messageId = frame.Header.Id ?? Guid.NewGuid();

        var transfer = NewTransfer(messageId, id, room.Name, name, session.ConnectionId, localJob: null, size);

        // The room hears the host's version of the announcement: this person, in this room, with this name.
        // What the sender chose to write in those fields is its own business.
        await RelayAsync(transfer, Frame.FileMeta(messageId, id, room.Name, session.Nickname, name, size)).ConfigureAwait(false);
    }

    private async Task ReceiveFileChunkAsync(Frame frame, ChatServerSession session)
    {
        if (frame.Header.TransferId is not { } id)
        {
            return;
        }

        var transfer = TransferFor(id);

        if (transfer is null)
        {
            // Bytes for a file nobody announced. There is no file to put them in, and making one up from a
            // number and a length is not something a host should do to a room.
            return;
        }

        if (transfer.Stopped)
        {
            return;
        }

        if (transfer.SenderConnectionId != session.ConnectionId)
        {
            session.Receive(Frame.Error("Only the machine sending a file can feed it."));
            return;
        }

        if (frame.Header.Sequence is not { } sequence)
        {
            StopTransfer(transfer, "a chunk arrived without a number");
            return;
        }

        if (transfer.Bytes + frame.Body.Length > transfer.SizeBytes)
        {
            // The room was promised a size. Relaying more than that would leave every receiver in a failure
            // none of them can fix, and this is the one place the lie is visible.
            StopTransfer(transfer, $"it sent more than the {transfer.SizeBytes:N0} bytes it promised");
            return;
        }

        transfer.Bytes += frame.Body.Length;

        await RelayAsync(transfer, Frame.FileChunk(id, sequence, frame.Body)).ConfigureAwait(false);
    }

    private async Task ReceiveFileDoneAsync(Frame frame, ChatServerSession session)
    {
        if (frame.Header.TransferId is not { } id)
        {
            return;
        }

        var transfer = TransferFor(id);

        if (transfer is null || transfer.Stopped)
        {
            return;
        }

        if (transfer.SenderConnectionId != session.ConnectionId)
        {
            session.Receive(Frame.Error("Only the machine sending a file can finish it."));
            return;
        }

        if (transfer.Bytes != transfer.SizeBytes)
        {
            // A short file announced as long would be saved as a broken file. Better a room that knows the
            // transfer failed than four windows each opening half a photograph.
            StopTransfer(transfer, $"it stopped after {transfer.Bytes:N0} of {transfer.SizeBytes:N0} bytes");
            return;
        }

        // "Done" is a frame the room has to hear too: it is the only thing that tells a receiver the last
        // chunk has gone by, and without it a window sits with a complete file and no idea that it is one.
        await RelayAsync(transfer, Frame.FileDone(id)).ConfigureAwait(false);

        transfer.Relay.Writer.TryComplete();

        // Waiting for the pump is what makes "done" mean done: every chunk has been handed over before the
        // frame that closes the file goes past it.
        await DrainAsync(transfer).ConfigureAwait(false);

        lock (gate)
        {
            transfers.Remove(id);
        }
    }

    private async Task ReceiveFileCancelAsync(Frame frame, ChatServerSession session)
    {
        if (frame.Header.TransferId is not { } id)
        {
            return;
        }

        var transfer = TransferFor(id);

        if (transfer is null || transfer.Stopped)
        {
            return;
        }

        if (transfer.SenderConnectionId == session.ConnectionId)
        {
            StopTransfer(transfer, $"{session.Nickname} stopped it");
            return;
        }

        // A recipient cancelling its own copy. The transfer belongs to the room rather than to that person,
        // so it carries on unless this was the last copy anybody wanted.
        DeclineFile(id, session);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ plumbing

    private Transfer NewTransfer(Guid messageId, Guid transferId, string roomName, string fileName, string senderConnectionId, FileTransferJob? localJob, long sizeBytes)
    {
        var transfer = new Transfer
        {
            Id = transferId,
            MessageId = messageId,
            RoomName = roomName,
            FileName = fileName,
            SenderConnectionId = senderConnectionId,
            SizeBytes = sizeBytes,
            LocalJob = localJob,
            Relay = Channel.CreateBounded<Frame>(new BoundedChannelOptions(LanternProtocol.FileQueueChunks)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            }),
        };

        lock (gate)
        {
            transfers[transferId] = transfer;
        }

        transfer.Pump = Task.Run(() => PumpTransferAsync(transfer));
        return transfer;
    }

    private Transfer? TransferFor(Guid id)
    {
        lock (gate)
        {
            return transfers.GetValueOrDefault(id);
        }
    }

    /// <summary>
    /// Takes frames out of one transfer's queue and hands them to the room, waiting while anybody who still
    /// wants the file is behind. This is the only place a file transfer is allowed to wait for a recipient,
    /// and it waits on the machine that is slow rather than on the ones keeping up.
    /// </summary>
    private async Task PumpTransferAsync(Transfer transfer)
    {
        try
        {
            await foreach (var frame in transfer.Relay.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (frame.Type == FrameType.FILE_CHUNK && !transfer.Stopped)
                {
                    if (!await WaitForRoomAsync(transfer).ConfigureAwait(false))
                    {
                        StopTransfer(transfer, "somebody could not receive it fast enough");
                    }

                    // Counted as it leaves the host, on the same rule a client follows on its own
                    // connection: a bar at 100% should mean the room has the bytes, not the sender.
                    transfer.LocalJob?.AddBytes(frame.Body.Length);
                }

                // Everything the transfer was given goes out in the order it was given, including after a
                // stop. An early exit here would drop whatever was still queued behind the stop, and the
                // first thing queued is nearly always the announcement - so a room that was fast enough to
                // still be reading the offer would never have known the file existed.
                foreach (var recipient in RecipientsFor(transfer))
                {
                    recipient.Receive(frame);
                }
            }
        }
        catch (Exception ex) when (ex is ChannelClosedException or OperationCanceledException)
        {
            // The only reason this loop ends early is that the transfer ended, which is already known.
        }
    }

    /// <summary>The members of this transfer's room who still want it, the sender excluded.</summary>
    private List<IParticipant> RecipientsFor(Transfer transfer)
    {
        lock (gate)
        {
            var room = rooms.FirstOrDefault(r => r.Name.Equals(transfer.RoomName, StringComparison.OrdinalIgnoreCase));

            if (room is null)
            {
                // The room was deleted while the file was on its way. Its members are nobody's members now.
                return [];
            }

            List<IParticipant> targets = [];

            foreach (var member in room.Members)
            {
                if (member.ConnectionId == transfer.SenderConnectionId || transfer.Declined.Contains(member.ConnectionId))
                {
                    continue;
                }

                if (participants.TryGetValue(member.ConnectionId, out var participant) && IsTaking(participant))
                {
                    targets.Add(participant);
                }
            }

            return targets;
        }
    }

    /// <summary>False for a connection that has started closing: its queue will never drain again.</summary>
    private static bool IsTaking(IParticipant participant)
        => participant is not ChatServerSession session || session.IsOpen;

    /// <summary>
    /// Waits until every recipient is holding less than the watermark of unwritten bytes, so a file cannot
    /// run ahead of the slowest machine taking it.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when somebody stayed behind for longer than
    /// <see cref="LanternProtocol.FileFlowTimeout"/>, which means the transfer ends rather than the wait.
    /// </returns>
    private async Task<bool> WaitForRoomAsync(Transfer transfer)
    {
        var watermark = (long)LanternProtocol.FileQueueChunks * FrameCodec.ChunkBytes;

        using var deadline = new CancellationTokenSource(LanternProtocol.FileFlowTimeout);

        while (true)
        {
            if (!IsBehind(transfer, watermark))
            {
                return true;
            }

            if (transfer.Stopped)
            {
                return false;
            }

            try
            {
                await Task.Delay(FlowPollInterval, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return !IsBehind(transfer, watermark);
            }
        }
    }

    private bool IsBehind(Transfer transfer, long watermark)
    {
        lock (gate)
        {
            return participants.Values.Any(p =>
                p is ChatServerSession session
                && session.IsOpen
                && session.QueuedBytes > watermark
                && !transfer.Declined.Contains(session.ConnectionId)
                && session.ConnectionId != transfer.SenderConnectionId
                && rooms.Any(r => r.Name.Equals(transfer.RoomName, StringComparison.OrdinalIgnoreCase) && r.HasMember(session.ConnectionId)));
        }
    }

    /// <summary>
    /// Puts a frame into a transfer's own queue, refusing to wait forever on a room that has stopped taking
    /// it. Whoever is producing the bytes feels that wait, which is the whole flow control.
    /// </summary>
    private async Task RelayAsync(Transfer transfer, Frame frame)
    {
        if (transfer.Stopped)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(LanternProtocol.FileFlowTimeout);

        try
        {
            await transfer.Relay.Writer.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ChannelClosedException or OperationCanceledException)
        {
            // The pump has stopped taking frames, either because the transfer was stopped or because the
            // room stopped draining. This frame is the last anyone will see, so the transfer says so out
            // loud rather than leaving a progress bar where a file should be.
            StopTransfer(transfer, "the room stopped taking it");
        }
    }

    private async Task DrainAsync(Transfer transfer)
    {
        try
        {
            if (transfer.Pump is not null)
            {
                await transfer.Pump.ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is ChannelClosedException or OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Ends a transfer for everybody: nobody gets another chunk, everybody who was getting one hears that it
    /// is over, and whoever was sending stops reading its disk.
    /// </summary>
    private void StopTransfer(Transfer transfer, string reason)
    {
        if (transfer.Stopped)
        {
            return;
        }

        transfer.Stopped = true;

        // Cancelled out loud. A receiver left with half a file and no news is worse than one told to stop,
        // and a sender cannot stop unless somebody tells it to.
        //
        // The notice goes into the queue rather than straight to the room, because the room reads a transfer
        // in order: a stop delivered ahead of the announcement it is stopping says nothing, since nobody has
        // been told about the file yet. Only if nothing will take it from the queue - because the pump has
        // already finished with this transfer - is it handed over directly instead.
        var notice = Frame.FileCancel(transfer.Id);
        var queued = transfer.Relay.Writer.TryWrite(notice);
        transfer.Relay.Writer.TryComplete();

        List<IParticipant> targets;

        lock (gate)
        {
            transfers.Remove(transfer.Id);
            targets = queued ? [] : [.. RecipientsFor(transfer)];
        }

        foreach (var target in targets)
        {
            target.Receive(notice);
        }

        if (transfer.LocalJob is { } job)
        {
            // The host was the sender: cancelling the job is what stops the reading, and the window sees it
            // as a job that stopped short of its own size.
            job.CancelTransfer();
        }
        else if (participants.TryGetValue(transfer.SenderConnectionId, out var sender))
        {
            // The sender is not a member of the room for this purpose - it has the file already - so this
            // one does go straight to them. It has to reach them whatever the room decides.
            sender.Receive(notice);
        }

        Status($"{transfer.FileName} did not go through: {reason}.");
    }

    /// <summary>
    /// One file on its way through the host. Not a message and not a domain object: it is the queue between
    /// whoever is producing the bytes and the pump that hands them out, plus the small facts the pump needs
    /// in order to know when to stop.
    /// </summary>
    private sealed class Transfer
    {
        public required Guid Id { get; init; }

        /// <summary>The id of the transcript line this file announced, kept so every window agrees on it.</summary>
        public required Guid MessageId { get; init; }

        public required string RoomName { get; init; }

        public required string FileName { get; init; }

        public required string SenderConnectionId { get; init; }

        public required Channel<Frame> Relay { get; init; }

        /// <summary>What FILE_META promised. Every receiver checks the same number at the end.</summary>
        public long SizeBytes { get; init; }

        /// <summary>Set when the host itself is sending, so a room that stops wanting it can stop the reading.</summary>
        public FileTransferJob? LocalJob { get; init; }

        /// <summary>People who have stopped wanting their copy; they get no further chunks (F8 Cancel).</summary>
        public HashSet<string> Declined { get; } = new(StringComparer.OrdinalIgnoreCase);

        public long Bytes { get; set; }

        /// <summary>True once the transfer has been ended, by anybody, for any reason.</summary>
        public bool Stopped { get; set; }

        public Task? Pump { get; set; }
    }
}
