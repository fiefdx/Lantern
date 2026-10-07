using Lantern.Domain;
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

    // File transfer (T5.2, T5.3) is what raises these; until then the interface needs them and the
    // host has nothing to say.
#pragma warning disable CS0067
    /// <inheritdoc />
    public event EventHandler<FileTransferJob>? IncomingFile;

    /// <inheritdoc />
    public event EventHandler<string>? FileSaved;

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
    public Task JoinRoomAsync(string? name, CancellationToken cancel = default)
    {
        // In process, so the frames arrive before this returns: no optimistic update, no round trip.
        server.Join(me, name);
        return Task.CompletedTask;
    }

    // ----------------------------------------------------------- room editing

    /// <inheritdoc />
    public Task CreateRoomAsync(string name, CancellationToken cancel = default)
        => server.TryCreateRoom(name, out var error) ? Task.CompletedTask : Throw(error);

    /// <inheritdoc />
    public Task RenameRoomAsync(string currentName, string newName, CancellationToken cancel = default)
        => server.TryRenameRoom(currentName, newName, out var error) ? Task.CompletedTask : Throw(error);

    /// <inheritdoc />
    public Task DeleteRoomAsync(string name, CancellationToken cancel = default)
        => server.TryDeleteRoom(name, out var error) ? Task.CompletedTask : Throw(error);

    private static Task Throw(string? error) => throw new InvalidOperationException(error ?? "That did not work.");

    // -------------------------------------------------------------- sending

    /// <inheritdoc />
    public Task SendAsync(Message message, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message is not TextMessage text)
        {
            throw new NotSupportedException("This build sends text messages only.");
        }

        if (ValidationRules.MessageError(text.Body) is { } error)
        {
            throw new InvalidOperationException(error);
        }

        // The server broadcasts to the room except its author, so this window's own copy is kept here
        // rather than sent around a loop to come back.
        state.Record(text);
        server.SendText(me, text.Body, text.Id, text.SentAt);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendFileAsync(string path, CancellationToken cancel = default)
        => throw new NotSupportedException("File transfer arrives with M5.");

    /// <inheritdoc />
    public Task SaveReceivedFileAsync(FileTransferJob job, string path, CancellationToken cancel = default)
        => throw new NotSupportedException("File transfer arrives with M5.");

    // ---------------------------------------------------------------- frames

    private void Apply(Frame frame)
    {
        if (frame.Type == FrameType.ERROR)
        {
            var problem = frame.Header.Error ?? "That was refused.";
            ServerError?.Invoke(this, problem);
            StatusChanged?.Invoke(this, problem);
            return;
        }

        if (state.Apply(frame) is { } message)
        {
            MessageReceived?.Invoke(this, message);
        }
    }

    public override string ToString() => $"host:{Nickname}";
}
