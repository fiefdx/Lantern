using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Lantern.Domain;

namespace Lantern.Core;

/// <summary>
/// In-memory <see cref="IChatService"/> holding dummy rooms and messages, so both windows can be
/// built, demoed and unit-tested without a single socket — let alone Bluetooth.
/// Events fire synchronously on the calling thread, which is what the real services will do too
/// once they have marshalled onto the UI thread.
/// <para>
/// Not sealed, and <see cref="SendAsync"/> is virtual, on purpose: a fake that cannot be varied is a
/// fixture. Some rules - Send and Share greyed out while a send is in flight (F3) - can only be seen from
/// inside a send that has not finished yet, and a test should be able to make one rather than write a
/// second service from scratch.
/// </para>
/// </summary>
public class FakeChatService : IChatService
{
    private const string DuplicateName = "A room with that name already exists.";
    private const string SelfId = "self";

    private readonly List<Message> transcript = [];

    private readonly List<string?> joins = [];

    public FakeChatService(string nickname, bool isHost = true)
    {
        Nickname = ValidationRules.Normalize(nickname);
        if (Nickname.Length == 0)
        {
            throw new ArgumentException("Nickname is required.", nameof(nickname));
        }

        IsHost = isHost;

        Rooms.CollectionChanged += OnRoomsChanged;
        Members.CollectionChanged += OnMembersChanged;

        // Dummy content in the shape of the wireframe: three rooms, people in the first one.
        var first = new Room("Room 1");
        first.AddMember(new User(SelfId, Nickname));
        first.AddMember(new User("peer1", "User 2"));
        first.AddMember(new User("peer2", "User 3"));
        Rooms.Add(first);
        Rooms.Add(new Room("Room 2"));
        Rooms.Add(new Room("Room 3"));

        foreach (var user in first.Members)
        {
            Members.Add(user);
        }

        Record(SystemMessage.Join("Room 1", "User 2"));
        Record(new TextMessage("User 3", "Room 1", "hi"));
        Record(new TextMessage(Nickname, "Room 1", "hi"));
    }

    public string Nickname { get; }

    public bool IsHost { get; }

    public ObservableCollection<Room> Rooms { get; } = [];

    public ObservableCollection<User> Members { get; } = [];

    // The UI sees snapshots: on a real client these lists are rewritten by a background read loop,
    // and handing out the live collection would let a window enumerate it half-updated.
    IReadOnlyList<Room> IChatService.Rooms => Rooms.ToArray();

    IReadOnlyList<User> IChatService.Members => Members.ToArray();

    /// <summary>Everything recorded so far, oldest first. Used by tests.</summary>
    public IReadOnlyList<Message> Transcript => transcript.ToArray();

    public event EventHandler<Message>? MessageReceived;

    public event EventHandler? RoomsChanged;

    public event EventHandler? MembersChanged;

    public event EventHandler<FileTransferJob>? IncomingFile;

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<Exception>? ConnectionLost;

    private readonly List<(FileTransferJob Job, string Path)> saves = [];

    /// <summary>No listening socket needed here; present so callers treat every service alike.</summary>
    public Task StartAsync(CancellationToken cancel = default)
    {
        StatusChanged?.Invoke(this, Nickname.Length > 0 ? "ready" : "offline");
        return Task.CompletedTask;
    }

    public Task ConnectAsync(ServerEndpoint endpoint, CancellationToken cancel = default) => Task.CompletedTask;

    public Task CreateRoomAsync(string name, CancellationToken cancel = default)
    {
        RequireHost();
        var normalized = ValidationRules.Normalize(name);
        Reject(ValidationRules.RoomNameError(normalized) ?? (IsDuplicate(normalized) ? DuplicateName : null));
        Rooms.Add(new Room(normalized));
        return Task.CompletedTask;
    }

    public Task RenameRoomAsync(Guid roomId, string newName, CancellationToken cancel = default)
    {
        RequireHost();
        var room = Rooms.FirstOrDefault(r => r.Id == roomId) ?? throw Fail("That room is not here any more.");
        var normalized = ValidationRules.Normalize(newName);
        Reject(ValidationRules.RoomNameError(normalized) ?? (IsDuplicate(normalized, room.Name) ? DuplicateName : null));

        // The same room under a new name: identity, members and transcript carried across, as in the real
        // server, because a test double that behaves differently about this is how a rename bug survives.
        var replacement = room.RenamedTo(normalized);

        var wasCurrent = IsCurrent(room);
        Rooms[Rooms.IndexOf(room)] = replacement;
        if (wasCurrent)
        {
            ReshowCurrent(replacement);
        }

        return Task.CompletedTask;
    }

    public Task DeleteRoomAsync(Guid roomId, CancellationToken cancel = default)
    {
        RequireHost();
        var room = Rooms.FirstOrDefault(r => r.Id == roomId);
        if (room is null)
        {
            return Task.CompletedTask;
        }

        var wasCurrent = IsCurrent(room);
        room.ClearMembers();
        Rooms.Remove(room);
        if (wasCurrent)
        {
            Members.Clear();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Closes a room the way a host's delete looks from a client's window: whoever was inside — this
    /// window's own user included — is left standing nowhere, and the room stops being listed. A client
    /// does not edit rooms, so this is how a test shows a window what its host did.
    /// </summary>
    /// <summary>
    /// Says something in the status bar, the way a real service does while it is working. The window forwards
    /// whatever it is told, so a test that cares about what the bar ends up showing needs to be able to make
    /// the service speak first.
    /// </summary>
    public void SimulateStatus(string message) => StatusChanged?.Invoke(this, message);

    public void SimulateRoomClosed(string name)
    {
        var room = Find(name);

        if (room is null)
        {
            return;
        }

        var wasCurrent = IsCurrent(room);
        room.ClearMembers();
        Rooms.Remove(room);

        if (wasCurrent)
        {
            Members.Clear();
        }
    }

    /// <summary>
    /// Every join this fake was asked for, in order, including the ones it refused. A window that asks twice for
    /// same room twice in a row has usually been asked by itself, and this is how a test tells that apart from
    /// a person who clicked twice.
    /// </summary>
    public IReadOnlyList<string?> JoinRequests => joins;

    /// <summary>
    /// Raise <see cref="RoomsChanged"/> from inside <see cref="JoinRoomAsync"/>, before it returns.
    /// <para>
    /// This is what the host's own window actually experiences: its chat engine is in process, so the room
    /// list arrives on the calling thread while the join is still on the stack. Off by default, because
    /// everything else in this fake answers afterwards, the way a client over a wire does.
    /// </para>
    /// </summary>
    public bool AnnounceRoomsOnJoin { get; set; }

    /// <summary>
    /// Refuse to let this window into the room of that name, the way a real server refuses a join it will not
    /// honour - and, like the real one, send the room list out with the refusal.
    /// </summary>
    public string? RefuseRoomNamed { get; set; }

    private void AnnounceRooms()
    {
        if (AnnounceRoomsOnJoin)
        {
            RoomsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public virtual Task JoinRoomAsync(Guid? roomId, CancellationToken cancel = default)
    {
        // Recorded by name, because that is what a test wants to read back; the room was asked for by
        // identity, and this is only a description of it.
        joins.Add(roomId is null ? null : Rooms.FirstOrDefault(r => r.Id == roomId)?.Name ?? "(gone)");

        var asked = roomId is null ? null : Rooms.FirstOrDefault(r => r.Id == roomId.Value);

        if (RefuseRoomNamed is { } refused && (asked is null ? false : string.Equals(asked.Name, refused, StringComparison.OrdinalIgnoreCase)))
        {
            AnnounceRooms();
            throw Fail($"Room \"{asked!.Name}\" will not have you.");
        }

        var previous = CurrentRoom();

        if (previous is not null && previous.Id != roomId)
        {
            previous.RemoveMember(Me(previous));
            Record(SystemMessage.Leave(previous.Name, Nickname));
        }

        Members.Clear();

        if (roomId is null)
        {
            AnnounceRooms();
            return Task.CompletedTask;
        }

        var room = asked ?? throw Fail("That room is not here any more.");
        if (room.AddMember(Me(room)))
        {
            Record(SystemMessage.Join(room.Name, Nickname));
        }

        foreach (var user in room.Members)
        {
            Members.Add(user);
        }

        AnnounceRooms();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records the message. The sender renders it locally, so no echo back up.
    /// <para>
    /// A styled line is judged on the way in, as it is on the wire: runs that do not add up to the sentence
    /// they annotate are dropped and the words are kept (see <see cref="SimulateIncoming"/> for why the fake
    /// insists on the same rules as the service it stands in for).
    /// </para>
    /// </summary>
    public virtual Task SendAsync(Message message, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message is TextMessage text && ValidationRules.MessageError(text.Body) is { } error)
        {
            throw Fail(error);
        }

        Record(WithAcceptedStyles(message));
        return Task.CompletedTask;
    }

    public FileTransferJob SendFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var size = File.Exists(path) ? new FileInfo(path).Length : 0L;
        var job = new FileTransferJob(Path.GetFileName(path), size, senderNickname: Nickname, roomName: CurrentRoom()?.Name ?? string.Empty);

        Record(new FileMessage(job.SenderNickname, job.RoomName, job.FileName, job.SizeBytes, job.Id, id: job.MessageId));

        // Nothing arrives from anywhere in a fake: the file is "sent" by finishing the job, which is what
        // lets a window's message be tested without a second machine. A window that has to *show* the
        // transfer needs the other setting, below.
        if (FilesCompleteImmediately)
        {
            AdvanceFile(job, job.SizeBytes);
        }

        return job;
    }

    /// <summary>
    /// Whether a file this fake sends is over as soon as it starts. Normally yes - a test about the message
    /// should not have to pump bytes - but a test about a progress bar needs a file that has not finished,
    /// which is the one thing a fake that always finishes cannot show.
    /// </summary>
    public bool FilesCompleteImmediately { get; set; } = true;

    /// <summary>Where each incoming file was asked to be put - the window's choice, seen from this side.</summary>
    public IReadOnlyList<(FileTransferJob Job, string Path)> SaveRequests => saves;

    public async Task SaveReceivedFileAsync(FileTransferJob job, string path, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        saves.Add((job, path));

        // Being told where to put a file is not the same as having it. Like the real receiver, this waits
        // for the last of the bytes and only then writes, so a window that reported "saved" before the file
        // had arrived would be caught by its own test rather than by a person noticing later.
        if (!job.IsComplete)
        {
            await job.Finished.WaitAsync(cancel).ConfigureAwait(false);
        }

        if (!job.IsComplete)
        {
            throw new InvalidOperationException($"{job.FileName} did not finish arriving.");
        }

        await File.WriteAllTextAsync(path, $"simulated payload for {job.FileName}", cancel).ConfigureAwait(false);
    }

    public Task ShutdownAsync()
    {
        foreach (var room in Rooms)
        {
            room.ClearMembers();
        }

        Members.Clear();
        StatusChanged?.Invoke(this, "offline");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pushes a message in "from the network": incoming traffic, sound notifier (F10).
    /// <para>
    /// "From the network" includes the rule that travels with it: a styled line is accepted or refused here
    /// the way the wire refuses it (see <see cref="ChatServer.SendText"/>), because a double with kinder
    /// rules than the thing it stands in for is how a window comes to pass a test the room fails.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ChatClient"/>, the fake lets a handler's exception through on purpose: it
    /// delivers on the caller's own thread, so a throw here is the test's own bug and should be seen
    /// rather than swallowed.
    /// </remarks>
    public void SimulateIncoming(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // The line handlers see is the line that was filed, so a test cannot be shown formatting that the
        // transcript refused to keep.
        var arriving = WithAcceptedStyles(message);
        Record(arriving);
        MessageReceived?.Invoke(this, arriving);
    }

    /// <summary>
    /// Makes a peer start sending a file, to demo the receiver side (F8). The job it hands back is only
    /// half a file: nothing has arrived yet, and <see cref="AdvanceIncomingFile" /> is what makes bytes
    /// turn up. That is on purpose - a window cannot be tested against a file that was always complete.
    /// </summary>
    public FileTransferJob SimulateIncomingFile(string fileName = "notes.txt", long sizeBytes = 4096, string from = "User 2")
    {
        var job = new FileTransferJob(fileName, sizeBytes, senderNickname: from, roomName: CurrentRoom()?.Name ?? string.Empty);
        IncomingFile?.Invoke(this, job);
        return job;
    }

    /// <summary>Lets some of a file arrive, the way chunks do, in either direction, so a bar can be watched.</summary>
    public void AdvanceFile(FileTransferJob job, long bytes)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Cancel.IsCancellationRequested)
        {
            // A stopped transfer stops receiving, which is the whole point of the cancel frame.
            return;
        }

        job.AddBytes(unchecked((int)Math.Min(int.MaxValue, bytes)));

        if (job.IsComplete)
        {
            job.Finish();
        }
    }

    /// <summary>
    /// Tells this side that a file in flight has been stopped by somebody else - the sender, the host, or a
    /// machine that could not drain fast enough (F8: the FILE_CANCEL frame arriving).
    /// </summary>
    public void SimulateFileStopped(FileTransferJob job, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(job);

        job.CancelTransfer();
        job.Finish();
        StatusChanged?.Invoke(this, reason ?? $"{job.FileName} was stopped by {job.SenderNickname}");
    }

    /// <summary>Drops the link the way a shut-down server does (scenario 5).</summary>
    public void SimulateConnectionLost(Exception? cause = null)
        => ConnectionLost?.Invoke(this, cause ?? new IOException("The connection to the server was lost."));

    /// <summary>
    /// Files a message in the flat log and, when its room still exists, in that room's own
    /// transcript — that is what lets the UI replay a room's history when the user re-enters it.
    /// </summary>
    private void Record(Message message)
    {
        transcript.Add(message);
        Find(message.RoomName)?.Append(message);
    }

    /// <summary>
    /// The message as this service will carry it, which for a styled line means with the formatting it
    /// earned: runs that do not add up to the sentence they annotate are dropped whole and the words are
    /// kept, exactly as they are on the wire. Everything else survives, its id included — the thing that was
    /// refused was never the message.
    /// </summary>
    /// <remarks>
    /// Applied wherever a line enters this service's views, in both directions. A fake that let an impossible
    /// run list through would be a second, gentler protocol for the windows to be tested against.
    /// </remarks>
    private static Message WithAcceptedStyles(Message message)
        => message is TextMessage { Styles.Count: > 0 } text && !StyledRun.Fits(text.Styles, text.Body)
            ? text.WithStyles([])
            : message;

    private User Me(Room room)
        => room.Members.FirstOrDefault(u => u.Nickname == Nickname) ?? new User(SelfId, Nickname);

    private Room? CurrentRoom() => Rooms.FirstOrDefault(RoomHasMe);

    private bool RoomHasMe(Room room) => room.Members.Any(u => u.Nickname == Nickname || u.ConnectionId == SelfId);

    private bool IsCurrent(Room room) => CurrentRoom()?.Name == room.Name;

    private void ReshowCurrent(Room room)
    {
        Members.Clear();
        foreach (var user in room.Members)
        {
            Members.Add(user);
        }
    }

    private Room? Find(string name)
        => Rooms.FirstOrDefault(r => string.Equals(r.Name, ValidationRules.Normalize(name), StringComparison.OrdinalIgnoreCase));

    private bool IsDuplicate(string name, string? ignore = null)
        => ValidationRules.IsDuplicateRoomName(
            name,
            Rooms.Where(r => ignore is null || !string.Equals(r.Name, ignore, StringComparison.OrdinalIgnoreCase)).Select(r => r.Name));

    private void RequireHost()
    {
        if (!IsHost)
        {
            throw Fail("Only the host can edit rooms.");
        }
    }

    private static void Reject(string? error)
    {
        if (error is not null)
        {
            throw Fail(error);
        }
    }

    private static InvalidOperationException Fail(string message) => new(message);

    private void OnRoomsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RoomsChanged?.Invoke(this, EventArgs.Empty);

    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs e) => MembersChanged?.Invoke(this, EventArgs.Empty);
}
      
