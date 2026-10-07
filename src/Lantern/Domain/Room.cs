using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Lantern.Domain;

/// <summary>
/// A named chat room (Figure 7). Only the name is ever persisted — the member list and the
/// transcript live exactly as long as the process does.
/// </summary>
public sealed class Room
{
    private readonly object gate = new();

    public Room(string name)
    {
        Name = ValidationRules.Normalize(name);
        if (Name.Length == 0)
        {
            throw new ArgumentException("Room name is required.", nameof(name));
        }

        Members = [];
        Members.CollectionChanged += OnMembersChanged;
    }

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

    public override string ToString() => $"{Name} ({Members.Count})";

    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs e) => MembersChanged?.Invoke(this, EventArgs.Empty);
}
