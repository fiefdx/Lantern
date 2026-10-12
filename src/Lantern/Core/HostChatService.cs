using Lantern.Domain;
using Lantern.Storage;
using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// The host window's view of its own chat (T2.7). The process that owns the
/// <see cref="ChatServer"/> still needs a window that shows rooms, members and messages, and the
/// honest way to do that is to make the window a member: it registers a
/// <see cref="LocalParticipant"/>, receives the same frames as any other participant, and sends
/// through the same code path.
/// <para>
/// That is why this class exists rather than the host window reading <see cref="ChatServer.Rooms"/>
/// directly: the server's state is its own, and a window that inspected it would need a second set of
/// change notifications and would drift the moment a room is renamed while it is redrawn.
/// </para>
/// </summary>
public sealed class HostChatService : IChatService, IAsyncDisposable
{
    private readonly ChatServer server;
    private readonly LocalParticipant me;
    private readonly ClientState state = new();
    private readonly FileReceiving files;

    public HostChatService(ChatServer server, string nickname)
    {
        this.server = server ?? throw new ArgumentNullException(nameof(server));

        Nickname = ValidationRules.Normalize(nickname);

        if (ValidationRules.NicknameError(Nickname) is { } error)
        {
            throw new ArgumentException(error, nameof(nickname));
        }

        me = new LocalParticipant(Nickname);
        me.FrameReceived += (_, frame) => Apply(frame);

        // The host is a member of its own room, so it receives other people's files exactly as a client
        // does - same spool, same notice, same rules - and this is where that is said.
        files = new FileReceiving(
            ReceivedPayloads.IncomingDirectory,
            job => IncomingFile?.Invoke(this, job),
            (job, path) => FileSaved?.Invoke(this, path),
            (_, reason) => Dispatch.Safe(() => StatusChanged?.Invoke(this, reason)),
            id => server.DeclineFile(id, me));

        state.RoomsChanged += (_, _) => RoomsChanged?.Invoke(this, EventArgs.Empty);
        state.MembersChanged += (_, _) => MembersChanged?.Invoke(this, EventArgs.Empty);
        server.StatusChanged += (_, status) => StatusChanged?.Invoke(this, status);
    }

    /// <inheritdoc />
    public string Nickname { get; }

    /// <inheritdoc />
    public bool IsHost => true;

    /// <inheritdoc />
    public IReadOnlyList<Room> Rooms => state.Rooms;

    /// <inheritdoc />
    public IReadOnlyList<User> Members => state.Members;

    /// <summary>The chat this window hosts, for the status line and for tests.</summary>
    public ChatServer Server => server;

    /// <summary>Where to reach this chat, once the server is listening.</summary>
    public ServerEndpoint Endpoint => server.Endpoint;

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

    // There is nobody to lose: the host is the server.
#pragma warning disable CS0067
    /// <inheritdoc />
    public event EventHandler<Exception>? ConnectionLost;
#pragma warning restore CS0067

    /// <summary>The host is the server, so there is nobody to lose.</summary>
    public event EventHandler<string>? ServerError;

    // ------------------------------------------------------------- lifecycle

    /// <summary>
    /// Joins the host's window to its own chat as a member. The server answers with a room list, so
    /// the window fills itself in from there exactly as a client would.
    /// </summary>
    public Task StartAsync(CancellationToken cancel = default)
    {
        if (!server.Register(me))
        {
            throw new InvalidOperationException("The server is not running, so there is nothing to join.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ConnectAsync(ServerEndpoint endpoint, CancellationToken cancel = default)
        => throw new InvalidOperationException("This window hosts the chat; it does not connect to one.");

    /// <summary>
    /// Stops the chat. Everyone connected sees the host go away (scenario 6), which is the point: the
    /// person hosting decides when it ends.
    /// </summary>
    public Task ShutdownAsync()
    {
        server.Unregister(me);
        return server.StopAsync();
    }

    /// <summary>Closing the window stops the chat, however the window was closed.</summary>
    public async ValueTask DisposeAsync() => await ShutdownAsync().ConfigureAwait(false);

    // ------------------------------------------------------------ room moves

    /// <inheritdoc />
    public Task JoinRoomAsync(Guid? roomId, CancellationToken cancel = default)
    {
        // In process, so the frames arrive before this returns: no optimistic update, no round trip.
        server.Join(me, roomId);
        return Task.CompletedTask;
    }

    // ----------------------------------------------------------- room editing

    /// <inheritdoc />
    public Task CreateRoomAsync(string name, CancellationToken cancel = default)
        => server.TryCreateRoom(name, out var error) ? Task.CompletedTask : Throw(error);

    /// <inheritdoc />
    public Task RenameRoomAsync(Guid roomId, string newName, CancellationToken cancel = default)
        => server.TryRenameRoom(roomId, newName, out var error) ? Task.CompletedTask : Throw(error);

    /// <inheritdoc />
    public Task DeleteRoomAsync(Guid roomId, CancellationToken cancel = default)
        => server.TryDeleteRoom(roomId, out var error) ? Task.CompletedTask : Throw(error);

    private static Task Throw(string? error) => throw new InvalidOperationException(error ?? "That did not work.");

    // -------------------------------------------------------------- sending

    /// <inheritdoc />
    public Task SendAsync(Message message, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        switch (message)
        {
            case TextMessage text:
                if (ValidationRules.MessageError(text.Body) is { } error)
                {
                    throw new InvalidOperationException(error);
                }

                // The server broadcasts to the room except its author, so this window's own copy is
                // kept here rather than sent around a loop to come back. The formatting travels with the
                // body instead of being left behind — or the host's window would be the only one in the room
                // that ever saw it — and this side holds its own runs to the same ruler as everyone else's,
                // because a host still showing bold that every client refused is two windows seeing two
                // different lines.
                var line = text.IsStyled && !StyledRun.Fits(text.Styles, text.Body)
                    ? text.WithStyles([])
                    : text;

                state.Record(line, state.CurrentRoomId ?? Guid.Empty);
                server.SendText(me, line.Body, line.Id, line.SentAt, line.Styles);
                break;

            case PictureMessage picture:
                // Recorded only if the server let it go. The room named inside the message is the room
                // the window was showing; the room the picture reaches is the room this participant is
                // in, and only the host's view of that counts - which is what makes two windows agree.
                if (server.SendPicture(me, picture.FileName, picture.Data, picture.Id, picture.SentAt) is { } sent)
                {
                    state.Record(sent, state.CurrentRoomId ?? Guid.Empty);
                }

                break;

            case FileMessage:
                // A file is not a message you can build and send: it is a transfer, and the bytes have to
                // come off a disk that this method has never seen. Say so rather than pretend.
                throw new InvalidOperationException("Send a file with SendFile, which reads it from disk.");

            default:
                throw new NotSupportedException("This build sends text and pictures.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public FileTransferJob SendFile(string path) => server.SendFile(me, path);

    /// <inheritdoc />
    public Task SaveReceivedFileAsync(FileTransferJob job, string path, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return files.SaveAsync(job, path, cancel);
    }

    // ---------------------------------------------------------------- frames

    private void Apply(Frame frame)
    {
        if (frame.Type == FrameType.ERROR)
        {
            var problem = frame.Header.Error ?? "That was refused.";
            Dispatch.Safe(() => ServerError?.Invoke(this, problem));
            Dispatch.Safe(() => StatusChanged?.Invoke(this, problem));
            return;
        }

        switch (frame.Type)
        {
            case FrameType.FILE_META:
                // The announcement goes in the transcript first, then the file starts arriving: the room's
                // history records that a file was offered whether or not anybody ends up keeping it (F8).
                Record(frame);
                files.Announce(frame);
                return;

            case FrameType.FILE_CHUNK:
                files.Chunk(frame);
                return;

            case FrameType.FILE_DONE:
                files.Complete(frame);
                return;

            case FrameType.FILE_CANCEL:
                files.CancelledByPeer(frame);
                return;
        }

        Record(frame);
    }

    private void Record(Frame frame)
    {
        // The host's window is reached synchronously from the server's own threads, so a throwing
        // handler there would otherwise unwind the server's broadcast. See Dispatch.
        if (state.Apply(frame) is { } message)
        {
            Dispatch.Safe(
                () => MessageReceived?.Invoke(this, message),
                ex => Dispatch.Safe(() => StatusChanged?.Invoke(this, $"A message could not be shown: {ex.Message}")));
        }
    }

    public override string ToString() => $"host:{Nickname}";
}
