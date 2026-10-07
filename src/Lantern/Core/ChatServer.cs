using System.Net.Sockets;
using Lantern.Domain;
using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// The host side of a chat (T2.4). One accept loop, one <see cref="ChatServerSession"/> per
/// connection, one room list, and a handful of synchronous decisions that turn a frame from one
/// client into frames for the others.
/// <para>
/// Everything that changes rooms happens in a lock and never awaits, so there is no window in which
/// a half-applied join could be observed by another read loop; frames are handed to
/// <see cref="IParticipant.Receive"/>, which queues them and returns immediately. Sending is therefore
/// always "queue and continue": a client that has stopped reading cannot stall a room.
/// </para>
/// It is deliberately not an <see cref="IChatService"/>: the host's own window joins through
/// <see cref="LocalParticipant"/>, so the person hosting is an ordinary room member like everyone
/// else (see <see cref="HostChatService"/>).
/// </summary>
public sealed class ChatServer : IAsyncDisposable
{
    private readonly IBtServer transport;
    private readonly object gate = new();
    private readonly List<Room> rooms = [];
    private readonly Dictionary<string, IParticipant> participants = [];
    private CancellationTokenSource? lifetime;
    private Task? acceptLoop;

    public ChatServer(IBtServer transport, string? serverName = null, IEnumerable<string>? roomNames = null)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        ServerName = string.IsNullOrWhiteSpace(serverName) ? Environment.MachineName : serverName.Trim();

        foreach (var name in roomNames ?? LanternProtocol.DefaultRoomNames)
        {
            rooms.Add(new Room(name));
        }
    }

    /// <summary>What the clients' server list shows.</summary>
    public string ServerName { get; }

    /// <summary>True between a successful start and <see cref="StopAsync"/>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Where to reach this server, for the host's status line and for a client to dial.</summary>
    public ServerEndpoint Endpoint => transport.Endpoint;

    /// <summary>Snapshot of the rooms. The list itself is only mutated under <see cref="gate"/>.</summary>
    public IReadOnlyList<Room> Rooms
    {
        get
        {
            lock (gate)
            {
                return rooms.ToArray();
            }
        }
    }

    /// <summary>Everyone connected right now, host window included.</summary>
    public IReadOnlyList<IParticipant> Participants
    {
        get
        {
            lock (gate)
            {
                return participants.Values.ToArray();
            }
        }
    }

    public int ConnectedCount
    {
        get
        {
            lock (gate)
            {
                return participants.Count;
            }
        }
    }

    /// <summary>Short text for the host's status bar.</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>The room list changed, so anything showing it (or persisting it) should refresh.</summary>
    public event EventHandler? RoomsChanged;

    /// <summary>A connection was accepted but never introduced itself (a scan probe, usually).</summary>
    public event EventHandler<string>? ProbeRejected;

    /// <summary>The accept loop stopped for a reason that is not a shutdown.</summary>
    public event EventHandler<Exception>? Faulted;

    // ------------------------------------------------------------- lifecycle

    /// <summary>Binds the transport and starts accepting.</summary>
    public async Task StartAsync(CancellationToken cancel = default)
    {
        lock (gate)
        {
            if (IsRunning)
            {
                return;
            }

            lifetime = new CancellationTokenSource();
        }

        await transport.StartAsync(LanternProtocol.ServiceId).ConfigureAwait(false);

        lock (gate)
        {
            IsRunning = true;
        }

        acceptLoop = AcceptLoopAsync(lifetime!.Token);
        Status("server online");
    }

    /// <summary>Stops listening and drops every connection — scenario 6.</summary>
    public async Task StopAsync()
    {
        CancellationTokenSource? source;

        lock (gate)
        {
            source = lifetime;
            lifetime = null;
            IsRunning = false;
        }

        source?.Cancel();
        transport.Stop();

        if (acceptLoop is not null)
        {
            try
            {
                await acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: the accept loop ends with the token.
            }

            acceptLoop = null;
        }

        source?.Dispose();
        Status("server stopped");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await transport.DisposeAsync().ConfigureAwait(false);
    }

    // ------------------------------------------------------------ room access

    /// <summary>Room with this name, case-insensitively, or null.</summary>
    public Room? FindRoom(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var wanted = ValidationRules.Normalize(name);
        lock (gate)
        {
            return rooms.FirstOrDefault(r => string.Equals(r.Name, wanted, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>The room this member is in, or null. The room's own member list is the only record.</summary>
    public Room? RoomOf(IParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        lock (gate)
        {
            return rooms.FirstOrDefault(r => r.HasMember(participant.ConnectionId));
        }
    }

    // ------------------------------------------------------- participant moves

    /// <summary>
    /// Registers a member with the server. The host's own window uses this; connected clients are
    /// registered by the handshake.
    /// </summary>
    public bool Register(IParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        lock (gate)
        {
            if (lifetime is null)
            {
                return false;
            }

            participants[participant.ConnectionId] = participant;
        }

        SendRooms(participant);
        Status($"{participant.Nickname} connected");
        return true;
    }

    /// <summary>
    /// Drops a member from every room it was in, telling the people left behind (F11, F12).
    /// </summary>
    public void Unregister(IParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        List<(Room Room, string Who)> left = [];

        lock (gate)
        {
            participants.Remove(participant.ConnectionId);

            foreach (var room in rooms)
            {
                if (room.RemoveMember(new User(participant.ConnectionId, participant.Nickname)))
                {
                    left.Add((room, participant.Nickname));
                }
            }
        }

        foreach (var (room, who) in left)
        {
            AnnounceLeave(room, who);
        }

        Status($"{participant.Nickname} disconnected");
    }

    /// <summary>
    /// Leave the current room (if any) and join <paramref name="roomName"/> (F6). A blank name means
    /// "join nothing", so one method covers leaving too and a member can never be in two rooms.
    /// </summary>
    public void Join(IParticipant participant, string? roomName)
    {
        ArgumentNullException.ThrowIfNull(participant);

        var wanted = ValidationRules.Normalize(roomName);

        Room? joined = null;
        Room? previous = null;

        lock (gate)
        {
            previous = rooms.FirstOrDefault(r => r.HasMember(participant.ConnectionId));

            if (previous is not null && !string.Equals(previous.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                previous.RemoveMember(new User(participant.ConnectionId, participant.Nickname));
            }
            else if (previous is not null && string.Equals(previous.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                // Already there: re-send the state instead of pretending the room changed.
                joined = previous;
            }

            if (joined is null && wanted.Length > 0)
            {
                joined = rooms.FirstOrDefault(r => string.Equals(r.Name, wanted, StringComparison.OrdinalIgnoreCase));

                if (joined is null)
                {
                    participant.Receive(Frame.Error($"No room named \"{wanted}\"."));
                    SendRooms(participant);
                    return;
                }

                joined.AddMember(new User(participant.ConnectionId, participant.Nickname));
            }
        }

        if (previous is not null && !ReferenceEquals(previous, joined))
        {
            AnnounceLeave(previous, participant.Nickname, exceptConnectionId: participant.ConnectionId);
        }

        if (joined is not null)
        {
            Broadcast(joined, Frame.SystemNotice(joined.Name, SystemMessage.JoinBody(participant.Nickname)));
            SendMemberList(joined);
        }
        else
        {
            participant.Receive(Frame.MemberList(string.Empty, []));
        }

        SendRooms(participant);
        Status($"{participant.Nickname} joined \"{joined?.Name ?? "no room"}\"");
    }

    /// <summary>
    /// A text message from <paramref name="sender"/> goes to the members of its room — and only to
    /// them (F6). The sender's own nickname is stamped on it here rather than trusted from the frame,
    /// so a client cannot put words in someone else's mouth.
    /// </summary>
    public void SendText(IParticipant sender, string body, Guid? id = null, DateTime? sentAt = null)
    {
        ArgumentNullException.ThrowIfNull(sender);

        if (ValidationRules.MessageError(body) is { } error)
        {
            sender.Receive(Frame.Error(error));
            return;
        }

        var room = RoomOf(sender);
        if (room is null)
        {
            sender.Receive(Frame.Error("Join a room before sending."));
            return;
        }

        var message = new TextMessage(sender.Nickname, room.Name, body, sentAt ?? DateTime.Now, id);
        Broadcast(room, Frame.FromMessage(message), exceptConnectionId: sender.ConnectionId);
    }

    /// <summary>A server-originated notice for a room (a room was deleted, someone was kicked).</summary>
    public void SendNotice(Room room, string notice)
    {
        ArgumentNullException.ThrowIfNull(room);

        Broadcast(room, Frame.SystemNotice(room.Name, notice));
    }

    // ----------------------------------------------------- room management

    /// <summary>
    /// Adds a room. Duplicate and invalid names are reported through <paramref name="error"/> instead
    /// of throwing, because the caller is a UI action.
    /// </summary>
    public bool TryCreateRoom(string name, out string? error)
    {
        error = null;
        var wanted = ValidationRules.Normalize(name);

        lock (gate)
        {
            error = ValidationRules.RoomNameError(wanted);

            if (error is null && rooms.Any(r => string.Equals(r.Name, wanted, StringComparison.OrdinalIgnoreCase)))
            {
                error = "A room with that name already exists.";
            }

            if (error is null)
            {
                rooms.Add(new Room(wanted));
            }
        }

        if (error is null)
        {
            BroadcastRooms();
        }

        return error is null;
    }

    /// <summary>Renames a room, keeping whoever is inside it.</summary>
    public bool TryRenameRoom(string currentName, string newName, out string? error)
    {
        error = null;
        var wanted = ValidationRules.Normalize(newName);
        Room? replacement = null;

        lock (gate)
        {
            var current = rooms.FirstOrDefault(r => string.Equals(r.Name, ValidationRules.Normalize(currentName), StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                error = $"No room named \"{currentName}\".";
                return false;
            }

            error = ValidationRules.RoomNameError(wanted);

            if (error is null && rooms.Any(r => r != current && string.Equals(r.Name, wanted, StringComparison.OrdinalIgnoreCase)))
            {
                error = "A room with that name already exists.";
            }

            if (error is not null)
            {
                return false;
            }

            replacement = new Room(wanted);
            foreach (var user in current.Members)
            {
                replacement.AddMember(user);
            }

            rooms[rooms.IndexOf(current)] = replacement;
        }

        BroadcastRooms();
        BroadcastMemberLists();
        return true;
    }

    /// <summary>
    /// Deletes a room. Anyone inside it is told they were removed (F11) and ends up in no room, which
    /// is what T3.3 asks for.
    /// </summary>
    public bool TryDeleteRoom(string name, out string? error)
    {
        error = null;
        Room? removed = null;

        lock (gate)
        {
            removed = rooms.FirstOrDefault(r => string.Equals(r.Name, ValidationRules.Normalize(name), StringComparison.OrdinalIgnoreCase));

            if (removed is null)
            {
                error = $"No room named \"{name}\".";
                return false;
            }

            rooms.Remove(removed);
        }

        var members = removed.Members.Select(m => m.ConnectionId).ToArray();
        removed.ClearMembers();

        foreach (var connectionId in members)
        {
            IParticipant? participant;
            lock (gate)
            {
                participants.TryGetValue(connectionId, out participant);
            }

            participant?.Receive(Frame.SystemNotice(removed.Name, $"{removed.Name} was closed by the host."));
            participant?.Receive(Frame.MemberList(string.Empty, []));
        }

        BroadcastRooms();
        return true;
    }

    /// <summary>Replaces the whole room list — used when it has just been loaded from disk (M3).</summary>
    public void LoadRooms(IEnumerable<string> roomNames)
    {
        ArgumentNullException.ThrowIfNull(roomNames);

        lock (gate)
        {
            rooms.Clear();

            foreach (var name in roomNames)
            {
                rooms.Add(new Room(name));
            }
        }

        BroadcastRooms();
    }

    // -------------------------------------------------------- frame handling

    /// <summary>
    /// What to do with a frame that arrived from a client. Runs on that connection's read loop, so it
    /// must not block: everything here either queues frames or takes the lock briefly.
    /// </summary>
    internal void Handle(Frame frame, ChatServerSession session)
    {
        switch (frame.Type)
        {
            case FrameType.JOIN_ROOM:
                Join(session, frame.Header.Room);
                break;

            case FrameType.TEXT:
                SendText(session, frame.Header.Text ?? string.Empty, frame.Header.Id, frame.Header.SentAt);
                break;

            case FrameType.HELLO:
                // Already introduced; two handshakes on one connection is a protocol error.
                session.Receive(Frame.Error("Already introduced."));
                break;

            default:
                // PICTURE, FILE_META / FILE_CHUNK / FILE_DONE and the server-only frame types.
                // Text is all M2 does; M5 opens these up (T5.2, T5.3).
                session.Receive(Frame.Error($"{frame.Type} frames are not supported by this host."));
                break;
        }
    }

    // --------------------------------------------------------------- plumbing

    private async Task AcceptLoopAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            IClientLink? link;

            try
            {
                link = await transport.AcceptAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                if (!cancel.IsCancellationRequested)
                {
                    Dispatch.Safe(() => Faulted?.Invoke(this, ex));
                }

                break;
            }

            if (link is null)
            {
                break;
            }

            // One task per connection: a client that stops talking must not stop the accept loop.
            _ = HandleConnectionAsync(link, cancel);
        }

        IsRunning = false;
    }

    private async Task HandleConnectionAsync(IClientLink link, CancellationToken cancel)
    {
        var session = new ChatServerSession(this, link);
        var introduced = false;

        try
        {
            introduced = await session.HandshakeAsync(cancel).ConfigureAwait(false);

            if (!introduced)
            {
                Dispatch.Safe(() => ProbeRejected?.Invoke(this, link.RemoteName));
                link.Close();
                return;
            }

            if (!Register(session))
            {
                session.Close();
                return;
            }

            var writes = session.WriteLoopAsync(cancel);
            var failure = await session.ReadLoopAsync(cancel).ConfigureAwait(false);

            session.TryComplete();

            try
            {
                await writes.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The writer's failure is the same disconnection the reader already reported.
            }

            if (failure is not null)
            {
                Status($"{session.Nickname} dropped: {failure.Message}");
            }
        }
        finally
        {
            if (introduced)
            {
                Unregister(session);
            }

            session.Close();
        }
    }

    private void Broadcast(Room room, Frame frame, string? exceptConnectionId = null)
    {
        List<IParticipant> targets = [];

        lock (gate)
        {
            foreach (var member in room.Members)
            {
                if (member.ConnectionId == exceptConnectionId)
                {
                    continue;
                }

                if (participants.TryGetValue(member.ConnectionId, out var participant))
                {
                    targets.Add(participant);
                }
            }
        }

        foreach (var target in targets)
        {
            target.Receive(frame);
        }
    }

    /// <summary>F12: everybody in a room gets the same member list, from the server's own view.</summary>
    private void SendMemberList(Room room)
    {
        var frame = Frame.MemberList(room.Name, room.Members);

        lock (gate)
        {
            foreach (var member in room.Members)
            {
                if (participants.TryGetValue(member.ConnectionId, out var participant))
                {
                    participant.Receive(frame);
                }
            }
        }
    }

    private void BroadcastMemberLists()
    {
        foreach (var room in Rooms)
        {
            if (room.Members.Count > 0)
            {
                SendMemberList(room);
            }
        }
    }

    /// <summary>T3.6: while the host edits rooms, every client's list follows live.</summary>
    private void BroadcastRooms()
    {
        var names = Rooms.Select(r => r.Name).ToArray();
        var frame = Frame.RoomList(names);

        lock (gate)
        {
            foreach (var participant in participants.Values)
            {
                participant.Receive(frame);
            }
        }

        Dispatch.Safe(() => RoomsChanged?.Invoke(this, EventArgs.Empty));
        Status($"{names.Length} rooms");
    }

    /// <summary>Sends the current room list to one member (after a handshake or a join).</summary>
    private void SendRooms(IParticipant participant)
        => participant.Receive(Frame.RoomList(Rooms.Select(r => r.Name).ToArray()));

    private void AnnounceLeave(Room room, string who, string? exceptConnectionId = null)
    {
        Broadcast(room, Frame.SystemNotice(room.Name, SystemMessage.LeaveBody(who)), exceptConnectionId);
        SendMemberList(room);
    }

    private void Status(string text) => Dispatch.Safe(() => StatusChanged?.Invoke(this, text));
}
