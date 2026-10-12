using System.Net.Sockets;
using Lantern.Domain;
using Lantern.Storage;
using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// The client side of a chat (T2.5): connects, introduces itself, then spends its life in a read loop
/// turning frames into state and events. It is an <see cref="IChatService"/>, so a window cannot tell
/// it apart from a host except through <see cref="IsHost"/>.
/// <para>
/// A file crosses the wire in both directions here (T5.3): <see cref="FileSending"/> pushes the bytes out
/// one frame at a time and <see cref="FileReceiving"/> spools the bytes coming in, so the window only ever
/// sees a job with progress on it. Nothing waits for a whole file in memory.
/// </para>
/// <para>
/// The read loop is the only writer of the state, and it runs on a thread-pool thread. That is why
/// <see cref="Rooms"/> and <see cref="Members"/> are snapshots and why every event is raised outside
/// the lock: a window must never enumerate a list that a MEMBER_LIST frame is half way through
/// replacing.
/// </para>
/// </summary>
public sealed class ChatClient : IChatService, IAsyncDisposable
{
    private readonly IBtClient transport;
    private readonly ClientState state = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly FileReceiving files;

    /// <summary>
    /// Files this machine is sending, by transfer id, so that a FILE_CANCEL coming back the other way can
    /// stop the reading. Without it a person who presses Cancel stops their own copy while the sender goes
    /// on pushing a whole file into a socket nobody is reading, and their progress bar carries on to 100%
    /// of something nobody received.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, FileTransferJob> outgoing = new();

    private IClientLink? link;
    private Task? readLoop;
    private TaskCompletionSource<Frame>? helloAck;
    private bool shuttingDown;

    public ChatClient(string nickname, IBtClient? transport = null)
    {
        Nickname = ValidationRules.Normalize(nickname);

        if (ValidationRules.NicknameError(Nickname) is { } error)
        {
            throw new ArgumentException(error, nameof(nickname));
        }

        this.transport = transport ?? new BtLoopbackClient();

        // Where the bytes go, who is told, and how a cancellation travels back. The last of those is why
        // Cancel has to be said out loud: without it the sender goes on filling a socket nobody reads.
        files = new FileReceiving(
            ReceivedPayloads.IncomingDirectory,
            job => IncomingFile?.Invoke(this, job),
            (job, path) => FileSaved?.Invoke(this, path),
            (_, reason) => Status(reason),
            id => _ = SendFrameAsync(Frame.FileCancel(id)));

        state.RoomsChanged += (_, _) => RoomsChanged?.Invoke(this, EventArgs.Empty);
        state.MembersChanged += (_, _) => MembersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public string Nickname { get; }

    /// <inheritdoc />
    public bool IsHost => false;

    /// <inheritdoc />
    public IReadOnlyList<Room> Rooms => state.Rooms;

    /// <inheritdoc />
    public IReadOnlyList<User> Members => state.Members;

    /// <summary>The server this client is, or was, talking to.</summary>
    public ServerEndpoint? Endpoint { get; private set; }

    /// <summary>Room this client is in, or null. Mirrors what the server believes.</summary>
    public string? CurrentRoomName => state.CurrentRoomName;

    /// <summary>Which room this side is in, by identity, or null. What a window compares; the name above is what it shows.</summary>
    public Guid? CurrentRoomId => state.CurrentRoomId;

    public bool IsConnected => link is not null;

    /// <inheritdoc />
    public event EventHandler<string>? StatusChanged;

    /// <inheritdoc />
    public event EventHandler<Message>? MessageReceived;

    /// <inheritdoc />
    public event EventHandler? RoomsChanged;

    /// <inheritdoc />
    public event EventHandler? MembersChanged;

    /// <inheritdoc />
    public event EventHandler<FileTransferJob>? IncomingFile;

    /// <inheritdoc />
    public event EventHandler<string>? FileSaved;

    /// <summary>
    /// The connection ended on its own (scenario 6). The exception's message is the reason, worded for
    /// the status bar rather than for a log.
    /// </summary>
    public event EventHandler<Exception>? ConnectionLost;

    /// <summary>The host refused something (an ERROR frame). Shown in the status bar.</summary>
    public event EventHandler<string>? ServerError;

    // ------------------------------------------------------------- connection

    /// <summary>Connects, says HELLO, waits for HELLO_ACK and starts the read loop (T2.5).</summary>
    public async Task ConnectAsync(ServerEndpoint endpoint, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (link is not null)
        {
            throw new InvalidOperationException("Already connected.");
        }

        Endpoint = endpoint;
        Status($"connecting to {endpoint}…");

        var newLink = await transport.ConnectAsync(endpoint, cancel).ConfigureAwait(false);
        link = newLink;

        var ack = new TaskCompletionSource<Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
        helloAck = ack;

        // The read loop has to be running before the HELLO goes out: the answer arrives on it.
        readLoop = Task.Run(() => ReadLoopAsync(newLink), CancellationToken.None);

        await WriteAsync(Frame.Hello(Nickname), cancel).ConfigureAwait(false);

        var completed = await Task.WhenAny(ack.Task, Task.Delay(LanternProtocol.HandshakeTimeout, cancel)).ConfigureAwait(false);

        if (completed != ack.Task)
        {
            await DisconnectAsync("The server did not answer the Lantern handshake.").ConfigureAwait(false);
            throw new InvalidOperationException("The server did not answer the Lantern handshake. Is it a Lantern host?");
        }

        var acknowledged = await ack.Task.ConfigureAwait(false);

        // Discovery can only report an address; HELLO_ACK is where the host says what to call it.
        if (acknowledged.Header.ServerName is { Length: > 0 } hostName)
        {
            Endpoint = Endpoint with { ServerName = hostName };
        }

        Status($"connected to {Endpoint}");
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancel = default)
        => throw new InvalidOperationException("A client does not listen for connections.");

    /// <inheritdoc />
    public async Task JoinRoomAsync(Guid? roomId, CancellationToken cancel = default)
    {
        state.ExpectRoom(roomId);

        // Nothing to join is how "leave the current room" travels on the wire.
        await WriteOrThrowAsync(Frame.JoinRoom(roomId), cancel).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendAsync(Message message, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        switch (message)
        {
            case TextMessage text:
                if (ValidationRules.MessageError(text.Body) is { } error)
                {
                    throw new InvalidOperationException(error);
                }

                await WriteOrThrowAsync(Frame.FromMessage(text, RoomOrThrow()), cancel).ConfigureAwait(false);

                // Kept as it will be seen. The host judges a run list against the sentence it annotates when
                // it fans the line out, so a list that does not add up is on its way to being thrown away by
                // everyone including the rest of this room — and this window keeping the bold for itself is
                // how two windows end up describing one message differently.
                Keep(text.IsStyled && !StyledRun.Fits(text.Styles, text.Body)
                    ? text.WithStyles([])
                    : text);
                break;

            case PictureMessage picture:
                // Checked on the way out as well as on the way in: a 40 MB photo should fail in this
                // window with these words, not as a refusal one round trip later.
                if (ValidationRules.PictureError(picture.FileName, picture.Data.LongLength) is { } pictureError)
                {
                    throw new InvalidOperationException(pictureError);
                }

                await WriteOrThrowAsync(Frame.FromMessage(picture, RoomOrThrow()), cancel).ConfigureAwait(false);
                Keep(picture);
                break;

            case FileMessage:
                // A FileMessage on its own only says "a file is coming", with no way to send it: the bytes
                // come off a disk, which is a transfer rather than a message. Say which door to use.
                throw new InvalidOperationException("Send a file with SendFile, which reads it from disk.");

            default:
                throw new NotSupportedException("This build sends text and pictures.");
        }
    }

    /// <summary>
    /// The host does not echo a message back to its author, so this side keeps its own copy or the
    /// transcript would lose every line the user sent when they next enter the room.
    /// </summary>
    private void Keep(Message message) => state.Keep(message);

    /// <summary>
    /// The room this side is standing in, as an object with an identity - which is what a frame needs in
    /// order to say which room it is carrying. Saying it by name would be saying which room this side thinks
    /// the name belongs to, and the host is the one that decides.
    /// </summary>
    private Lantern.Domain.Room RoomOrThrow()
    {
        // The link is the more basic fact. A window whose connection has gone does have a room it is standing
        // in and no way to reach it, and telling such a person to pick a room would be a sentence about the
        // wrong thing - and about a room they are still standing in.
        if (link is null)
        {
            throw NotConnected();
        }

        return state.FindRoom(state.CurrentRoomId) ?? throw new InvalidOperationException("Pick a room first.");
    }

    // -------------------------------------------------------- host-only actions

    /// <inheritdoc />
    public Task CreateRoomAsync(string name, CancellationToken cancel = default) => HostOnly();

    /// <inheritdoc />
    public Task RenameRoomAsync(Guid roomId, string newName, CancellationToken cancel = default) => HostOnly();

    /// <inheritdoc />
    public Task DeleteRoomAsync(Guid roomId, CancellationToken cancel = default) => HostOnly();

    /// <inheritdoc />
    public FileTransferJob SendFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var room = state.FindRoom(state.CurrentRoomId) ?? throw new InvalidOperationException("Pick a room before sending a file.");
        var file = Path.GetFullPath(path);

        if (!File.Exists(file))
        {
            throw new FileNotFoundException("That file is not there any more.", file);
        }

        if (new FileInfo(file).Length > LanternProtocol.MaxReceivedFileBytes)
        {
            // Turned down here, while the person is still looking at the folder they picked from. The host
            // refuses it too, but a file that big should not have to travel to the host and back before
            // anybody says so.
            throw new InvalidOperationException($"Lantern does not send files over {FileSize.Describe(LanternProtocol.MaxReceivedFileBytes)}.");
        }

        var job = new FileTransferJob(Path.GetFileName(file), new FileInfo(file).Length, senderNickname: Nickname, roomName: room.Name, roomId: room.Id);

        // The window is not asked to hold still for a file it cannot watch: what it gets back is the job,
        // which is the thing to watch, and the bytes go on moving afterwards on their own thread.
        _ = Task.Run(() => StreamFileAsync(job, file));
        return job;
    }

    /// <inheritdoc />
    public Task SaveReceivedFileAsync(FileTransferJob job, string path, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return files.SaveAsync(job, path, cancel);
    }

    private static Task HostOnly() => throw new InvalidOperationException("Only the host can edit rooms.");

    // ------------------------------------------------------------ shutdown

    /// <inheritdoc />
    public async Task ShutdownAsync()
    {
        shuttingDown = true;
        await DisconnectAsync(null).ConfigureAwait(false);
        Status("disconnected");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);

        if (transport is IDisposable disposable)
        {
            disposable.Dispose();
        }

        writeLock.Dispose();
    }

    /// <summary>How long hanging up is allowed to take before this window is allowed to be on its way out.</summary>
    private static readonly TimeSpan LinkGrace = TimeSpan.FromSeconds(1);

    private async Task DisconnectAsync(string? reason)
    {
        var current = Interlocked.Exchange(ref link, null);

        if (current is not null)
        {
            // Hanging up. On a radio this is where the time goes: the stack wants a polite goodbye from a
            // machine that may be switched off, and it will wait for it. So it is started, waited on briefly,
            // and then left to finish on its own — the channel is nobody's business by now but its own.
            var closing = Task.Run(current.Close);

            try
            {
                closing.Wait(LinkGrace);
            }
            catch (Exception)
            {
                // A link that complains while being closed is a link that is closing.
            }
        }

        var loop = Interlocked.Exchange(ref readLoop, null);

        if (loop is not null)
        {
            try
            {
                // Bounded for the same reason: a read still parked on a channel that has gone is unblocked by
                // the close above, and if the stack has not got round to it yet, waiting longer here is only
                // waiting on the stack.
                await loop.WaitAsync(LinkGrace).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
            catch (Exception)
            {
                // The read loop reports its own reasons; nothing to add here.
            }
        }

        helloAck?.TrySetCanceled();

        // Files that were arriving stopped arriving when the link did. Say so, throw the half files away, and
        // let any window watching one stop counting: a bar sitting at 40% of a file that will never have the
        // rest of it is a lie, and a .part left in the incoming folder is found next month rather than now.
        files.AbandonUnfinished(reason ?? "the connection closed");

        if (reason is not null)
        {
            Status(reason);
        }
    }

    // ------------------------------------------------------------ the read loop

    private async Task ReadLoopAsync(IClientLink owned)
    {
        string? reason = null;

        try
        {
            while (true)
            {
                var frame = await FrameCodec.ReadAsync(owned.Input, CancellationToken.None).ConfigureAwait(false);

                if (frame is null)
                {
                    reason = "The host closed the connection.";
                    break;
                }

                Handle(frame);
            }
        }
        catch (Exception ex) when (ex is FrameException or IOException or SocketException or ObjectDisposedException)
        {
            reason = ex is FrameException frameError
                ? $"The host sent something unreadable: {frameError.Message}"
                : "Connection lost.";
        }

        var wasCurrent = ReferenceEquals(Volatile.Read(ref link), owned);
        _ = Interlocked.CompareExchange(ref link, null, owned);

        // Whatever the transcripts hold stays; who is in a room is the server's business, and there is
        // no server any more.
        state.ClearMembership();

        // A file that was arriving on this link stopped arriving when the link did, whoever ended it. Say so
        // and throw the half of it away: left alone, it is a progress bar that has stopped moving for ever
        // and a .part in the incoming folder that somebody finds next month.
        files.AbandonUnfinished(reason ?? "the connection closed");

        if (wasCurrent && !shuttingDown)
        {
            reason ??= "Connection lost.";
            Dispatch.Safe(() => ConnectionLost?.Invoke(this, new IOException(reason)));
            Status(reason);
        }
    }

    private void Handle(Frame frame)
    {
        switch (frame.Type)
        {
            case FrameType.HELLO_ACK:
                helloAck?.TrySetResult(frame);
                break;

            case FrameType.ERROR:
                var problem = frame.Header.Error ?? "The host rejected that.";
                Dispatch.Safe(() => ServerError?.Invoke(this, problem));
                Status(problem);
                break;

            case FrameType.FILE_META:
                // The announcement goes into the transcript first, then the file starts arriving: the
                // room's history records that a file was offered whether or not anybody keeps it (F8).
                Record(frame);
                files.Announce(frame);
                break;

            case FrameType.FILE_CHUNK:
                files.Chunk(frame);
                break;

            case FrameType.FILE_DONE:
                files.Complete(frame);
                break;

            case FrameType.FILE_CANCEL:
                // Either somebody over there stopped wanting a file we are receiving, or we are the one
                // sending it and they have just stopped wanting it. The frame looks the same either way,
                // and only the transfer id says which.
                if (frame.Header.TransferId is { } stopped && outgoing.TryGetValue(stopped, out var sending))
                {
                    sending.CancelTransfer();
                }
                else
                {
                    files.CancelledByPeer(frame);
                }

                break;

            default:
                // ROOM_LIST, MEMBER_LIST and the traffic frames.
                // The state is updated first, so a window that throws has already been accounted for and
                // nothing is left half-applied; only the notification is lost. See Dispatch.
                if (state.Apply(frame) is { } message)
                {
                    Dispatch.Safe(
                        () => MessageReceived?.Invoke(this, message),
                        ex => Status($"A message could not be shown: {ex.Message}"));
                }

                break;
        }
    }

    /// <summary>
    /// Streams a file out on its own thread. Every chunk is awaited on its way out, so when the link is
    /// slower than the disk it is the socket buffer that fills and this loop that slows down: the flow
    /// control Windows gives for free, and the reason a big file cannot run ahead of the connection
    /// carrying it.
    /// </summary>
    private async Task StreamFileAsync(FileTransferJob job, string file)
    {
        outgoing[job.Id] = job;

        try
        {
            await FileSending
                .StreamAsync(job, file, frame => WriteAsync(frame, CancellationToken.None), job.Cancel.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException or FrameException)
        {
            // The link went away in the middle of the file. That is worth a sentence: the person knows they
            // chose the file, and a bar that quietly stopped is not an answer.
            Status($"{job.FileName} did not go: {ex.Message}");
        }
        catch (Exception ex) when (ex is OperationCanceledException)
        {
            // Cancelled, which FileSending has already said out loud to the room.
        }
        finally
        {
            outgoing.TryRemove(job.Id, out _);
            job.Finish();
        }
    }

    /// <summary>Applies a frame to the local state and shows whatever transcript line it carries.</summary>
    private void Record(Frame frame)
    {
        if (state.Apply(frame) is { } message)
        {
            Dispatch.Safe(
                () => MessageReceived?.Invoke(this, message),
                ex => Status($"A message could not be shown: {ex.Message}"));
        }
    }

    // ---------------------------------------------------------------- writing

    /// <summary>The sentence for a frame with nowhere to go, said the same way wherever it is found.</summary>
    private static InvalidOperationException NotConnected() => new("Not connected.");

    private async Task WriteOrThrowAsync(Frame frame, CancellationToken cancel)
    {
        if (link is null)
        {
            throw NotConnected();
        }

        try
        {
            await WriteAsync(frame, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            throw new InvalidOperationException("Connection lost.", ex);
        }
    }

    /// <summary>
    /// One frame at a time on the wire. Two messages written by interleaving half-frames would be
    /// unreadable at the other end, and the UI can raise SendAsync faster than one call completes.
    /// </summary>
    /// <summary>Writes one frame, used by paths that are not a message the window sent (a cancellation).</summary>
    private async Task SendFrameAsync(Frame frame)
    {
        try
        {
            await WriteAsync(frame, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
        {
            // Telling the host we stopped matters, but not enough to fall over when the link is the reason.
            Status(ex.Message);
        }
    }

    private async Task WriteAsync(Frame frame, CancellationToken cancel)
    {
        var current = link;

        if (current is null)
        {
            throw new InvalidOperationException("Not connected.");
        }

        await writeLock.WaitAsync(cancel).ConfigureAwait(false);

        try
        {
            await FrameCodec.WriteAsync(current.Output, frame, cancel).ConfigureAwait(false);
            await current.Output.FlushAsync(cancel).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private void Status(string text) => Dispatch.Safe(() => StatusChanged?.Invoke(this, text));

    public override string ToString() => $"{Nickname} → {Endpoint}";
}
