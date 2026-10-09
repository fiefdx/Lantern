using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Lantern.Domain;

/// <summary>
/// A named chat room (Figure 7). Only the id and the name are ever persisted — the member list and the
/// transcript live exactly as long as the process does.
/// </summary>
/// <remarks>
/// A room has two names for itself, and they are not interchangeable. <see cref="Name"/> is the one a
/// person reads, types and may change at any moment (F5 lets the host rename a room); <see cref="Id"/> is
/// the one that says which room is meant, and it is what every lookup, every frame and <c>rooms.json</c>
/// use. Keying things on the name is not a shortcut: the name moves, and a room keyed on it turns into two
/// rooms the moment somebody renames it.
/// </remarks>
public sealed class Room
{
    private readonly object gate = new();

    /// <summary>A room this program has just thought of, so it mints its own identity.</summary>
    public Room(string name) : this(Guid.NewGuid(), name)
    {
    }

    /// <summary>
    /// The identity is handed in rather than minted when the room is being <em>restored</em> — out of
    /// <c>rooms.json</c>, out of a <c>ROOM_LIST</c> frame, or under a new name — because in those cases the
    /// room already has one and the whole point is to keep it.
    /// </summary>
    public Room(Guid id, string name)
    {
        Id = id;
        Name = ValidationRules.Normalize(name);
        if (Name.Length == 0)
        {
            throw new ArgumentException("Room name is required.", nameof(name));
        }

        Members = [];
        Members.CollectionChanged += OnMembersChanged;
    }

    /// <summary>
    /// Which room this is. Stable for as long as the room exists, through a rename and through a restart.
    /// Nothing in this program identifies a room by its name any more than it identifies a person by their
    /// nickname.
    /// </summary>
    public Guid Id { get; }

    public string Name { get; }

    /// <summary>Who is inside this room right now (F12). Never persisted.</summary>
    public ObservableCollection<User> Members { get; }

    /// <summary>Raised whenever <see cref="Members"/> changes, on the mutating thread.</summary>
    public event EventHandler? MembersChanged;

    /// <summary>Raised whenever a message is added to the transcript.</summary>
    public event EventHandler<Message>? MessageAppended;

    private readonly List<Message> transcript = [];

    /// <summary>Snapshot of the in-memory transcript, oldest first.</summary>
    public IReadOnlyList<Message> Transcript
    {
        get
        {
            lock (gate)
            {
                return transcript.ToArray();
            }
        }
    }

    public void Append(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.RoomName != Name)
        {
            throw new ArgumentException($"Message belongs to room \"{message.RoomName}\", not \"{Name}\".", nameof(message));
        }

        Adopt(message);
    }

    /// <summary>
    /// Adds a line that was <em>routed here by identity</em>, so the name it quotes may be one this room no
    /// longer answers to: a rename can arrive between a message being sent and this side hearing the new name.
    /// <see cref="Append"/> calls that a programming error, and here it would be wrong.
    /// </summary>
    internal void Adopt(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (gate)
        {
            transcript.Add(message);
        }

        MessageAppended?.Invoke(this, message);
    }

    public bool AddMember(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        lock (gate)
        {
            if (Members.Any(m => m.ConnectionId == user.ConnectionId))
            {
                return false;
            }

            Members.Add(user);
        }

        return true;
    }

    public bool RemoveMember(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        lock (gate)
        {
            var existing = Members.FirstOrDefault(m => m.ConnectionId == user.ConnectionId);
            if (existing is null)
            {
                return false;
            }

            Members.Remove(existing);
        }

        return true;
    }

    /// <summary>Remove everyone (used when the room is deleted while people are inside).</summary>
    public void ClearMembers()
    {
        lock (gate)
        {
            Members.Clear();
        }
    }

    public bool HasMember(string connectionId)
    {
        lock (gate)
        {
            return Members.Any(m => m.ConnectionId == connectionId);
        }
    }

    /// <summary>
    /// The same room, called something else: the same <see cref="Id"/>, the same members and the same
    /// transcript, in a new object.
    /// <para>
    /// A new object rather than a writable <see cref="Name"/> because the transcript quotes the name its
    /// room had when each line was written, and a rename must not rewrite history. It is still one room:
    /// keyed on the name, a rename looks exactly like a room being deleted and a different room built in
    /// its place, which is what used to happen to everyone standing in it.
    /// </para>
    /// </summary>
    internal Room RenamedTo(string newName)
    {
        var renamed = new Room(Id, newName);
        renamed.AdoptTranscriptFrom(this);

        foreach (var user in Members.ToArray())
        {
            renamed.AddMember(user);
        }

        return renamed;
    }

    /// <summary>
    /// Takes over another room's lines. Past <see cref="Append"/>, deliberately: those lines name the room
    /// as it was called when they were written, and that is not a reason to throw them away.
    /// </summary>
    private void AdoptTranscriptFrom(Room previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        lock (gate)
        {
            transcript.AddRange(previous.Transcript);
        }
    }

    public override string ToString() => $"{Name} ({Members.Count})";

    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs e) => MembersChanged?.Invoke(this, EventArgs.Empty);
}
