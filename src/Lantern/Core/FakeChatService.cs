using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Lantern.Domain;

namespace Lantern.Core;

/// <summary>
/// In-memory <see cref="IChatService"/> holding dummy rooms and messages, so both windows can be
/// built, demoed and unit-tested without a single socket — let alone Bluetooth.
/// Events fire synchronously on the calling thread, which is what the real services will do too
/// once they have marshalled onto the UI thread.
/// </summary>
public sealed class FakeChatService : IChatService
{
    private const string DuplicateName = "A room with that name already exists.";
    private const string SelfId = "self";

    private readonly List<Message> transcript = [];

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

    /// <summary>Everything recorded so far, oldest first. Used by tests.</summary>
    public IReadOnlyList<Message> Transcript => transcript.ToArray();

    public event EventHandler<Message>? MessageReceived;

    public event EventHandler? RoomsChanged;

    public event EventHandler? MembersChanged;

    public event EventHandler<FileTransferJob>? IncomingFile;

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<Exception>? ConnectionLost;

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

    public Task RenameRoomAsync(string currentName, string newName, CancellationToken cancel = default)
    {
        RequireHost();
        var room = Find(currentName) ?? throw Fail($"No room named \"{currentName}\".");
        var normalized = ValidationRules.Normalize(newName);
        Reject(ValidationRules.RoomNameError(normalized) ?? (IsDuplicate(normalized, currentName) ? DuplicateName : null));

        // Names identify rooms, so a rename rebuilds the entry in place, members included.
        var replacement = new Room(normalized);
        foreach (var user in room.Members)
        {
            replacement.AddMember(user);
        }

        var wasCurrent = IsCurrent(room);
        Rooms[Rooms.IndexOf(room)] = replacement;
        if (wasCurrent)
        {
            ReshowCurrent(replacement);
        }

        return Task.CompletedTask;
    }

    public Task DeleteRoomAsync(string name, CancellationToken cancel = default)
    {
        RequireHost();
        var room = Find(name);
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

    public Task JoinRoomAsync(string? name, CancellationToken cancel = default)
    {
        var previous = CurrentRoom();

        if (previous is not null && !string.Equals(previous.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            previous.RemoveMember(Me(previous));
            Record(SystemMessage.Leave(previous.Name, Nickname));
        }

        Members.Clear();

        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.CompletedTask;
        }

        var room = Find(name) ?? throw Fail($"No room named \"{name}\".");
        if (room.AddMember(Me(room)))
        {
            Record(SystemMessage.Join(room.Name, Nickname));
        }

        foreach (var user in room.Members)
        {
            Members.Add(user);
        }

        return Task.CompletedTask;
    }

    /// <summary>Records the message. The sender renders it locally, so no echo back up.</summary>
    public Task SendAsync(Message message, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message is TextMessage text && ValidationRules.MessageError(text.Body) is { } error)
        {
            throw Fail(error);
        }

        Record(message);
        return Task.CompletedTask;
    }

    public Task SendFileAsync(string path, CancellationToken cancel = default)
    {
        var size = File.Exists(path) ? new FileInfo(path).Length : 0L;
        return SendAsync(new FileMessage(Nickname, CurrentRoom()?.Name ?? string.Empty, Path.GetFileName(path), size), cancel);
    }

    public Task SaveReceivedFileAsync(FileTransferJob job, string path, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        File.WriteAllText(path, $"simulated payload for {job.FileName}");
        while (!job.IsComplete)
        {
            job.AddBytes(unchecked((int)Math.Min(int.MaxValue, job.SizeBytes - job.BytesDone)));
        }

        return Task.CompletedTask;
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

    /// <summary>Pushes a message in "from the network": incoming traffic, sound notifier (F10).</summary>
    public void SimulateIncoming(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Record(message);
        MessageReceived?.Invoke(this, message);
    }

    /// <summary>Makes a peer start sending a file, to demo the receiver side (F8).</summary>
    public FileTransferJob SimulateIncomingFile(string fileName = "notes.txt", long sizeBytes = 4096)
    {
        var job = new FileTransferJob(fileName, sizeBytes, senderNickname: "User 2", roomName: CurrentRoom()?.Name);
        IncomingFile?.Invoke(this, job);
        return job;
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
      
