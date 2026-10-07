using Lantern.Domain;
using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// What one participant knows about the chat: the room list, who is in the room it is standing in,
/// and a transcript per room. Frames go in, state and change notifications come out.
/// <para>
/// Both sides of a window need this. A remote client fills it from the frames its socket delivers;
/// the host's own window fills it from the frames <see cref="ChatServer"/> hands it in process
/// (<see cref="HostChatService"/>). Duplicating the mapping would mean two places to get wrong, and
/// the interesting bugs — which room a member list describes, what an empty list means, whether a
/// rename keeps the transcript — are exactly the ones that show up as a window lying about its own
/// state.
/// </para>
/// Every method may be called from a background read loop while the UI reads the snapshots, so the
/// lists are handed out as copies.
/// </summary>
internal sealed class ClientState
{
    private readonly object gate = new();
    private readonly Dictionary<string, Room> roomsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Room> rooms = [];
    private readonly List<User> members = [];
    private string? currentRoomName;

    /// <summary>Raised after the room list changes. The window re-reads <see cref="Rooms"/>.</summary>
    public event EventHandler? RoomsChanged;

    /// <summary>Raised after the member list changes. The window re-reads <see cref="Members"/>.</summary>
    public event EventHandler? MembersChanged;

    /// <summary>Snapshot of the rooms, in the order the host listed them.</summary>
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

    /// <summary>Snapshot of who is in the current room.</summary>
    public IReadOnlyList<User> Members
    {
        get
        {
            lock (gate)
            {
                return members.ToArray();
            }
        }
    }

    /// <summary>The room this side believes it is in, or null.</summary>
    public string? CurrentRoomName
    {
        get
        {
            lock (gate)
            {
                return currentRoomName;
            }
        }
    }

    /// <summary>The room object for a name, if it is known.</summary>
    public Room? FindRoom(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (gate)
        {
            return roomsByName.GetValueOrDefault(name);
        }
    }

    /// <summary>
    /// Applies a frame from the server.
    /// </summary>
    /// <returns>
    /// The message a traffic frame carried, already stored in its room's transcript, or
    /// <see langword="null"/> for list frames and anything this side has no use for.
    /// </returns>
    public Message? Apply(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        switch (frame.Type)
        {
            case FrameType.ROOM_LIST:
                ApplyRoomList(frame.Header.Rooms);
                return null;

            case FrameType.MEMBER_LIST:
                ApplyMemberList(frame.Header.Room, frame.Header.Members);
                return null;

            case FrameType.TEXT:
            case FrameType.PICTURE:
            case FrameType.FILE_META:
            case FrameType.SYSTEM_NOTICE:
                var message = frame.ToMessage();

                if (message is not null)
                {
                    Record(message);
                }

                return message;

            default:
                return null;
        }
    }

    /// <summary>
    /// Records something this side said. The server does not echo a message back to its author, so
    /// without this, re-entering a room would replay everyone else's lines and lose your own.
    /// </summary>
    public void Record(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(message.RoomName))
        {
            return;
        }

        lock (gate)
        {
            if (!roomsByName.TryGetValue(message.RoomName, out var room))
            {
                // Traffic about a room we have not been told about yet. Keep it: the next ROOM_LIST
                // decides whether the room is shown, and a message that arrived is evidence the room
                // exists even if the list is one frame behind.
                room = new Room(message.RoomName);
                roomsByName[message.RoomName] = room;
            }

            room.Append(message);
        }
    }

    /// <summary>
    /// Sets the current room as soon as the request to join leaves, so the window does not wait for
    /// the round trip to enable itself. Every reply corrects it if the host refused.
    /// </summary>
    public void ExpectRoom(string? name)
    {
        var wanted = ValidationRules.Normalize(name);

        lock (gate)
        {
            currentRoomName = wanted.Length == 0 ? null : wanted;
        }
    }

    /// <summary>Forgets the membership without forgetting the transcripts (a connection ended).</summary>
    public void ClearMembership()
    {
        lock (gate)
        {
            members.Clear();
            currentRoomName = null;
        }

        MembersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyRoomList(IReadOnlyList<string>? names)
    {
        if (names is null)
        {
            return;
        }

        lock (gate)
        {
            rooms.Clear();

            foreach (var name in names)
            {
                // Rooms survive a trip through the list by name, so a room's transcript is not thrown
                // away every time the host re-sends the list.
                if (!roomsByName.TryGetValue(name, out var room))
                {
                    room = new Room(name);
                    roomsByName[name] = room;
                }

                rooms.Add(room);
            }

            if (currentRoomName is not null && !roomsByName.ContainsKey(currentRoomName))
            {
                // The host closed the room this side was standing in.
                currentRoomName = null;
            }
        }

        RoomsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyMemberList(string? roomName, IReadOnlyList<FrameMember>? incoming)
    {
        lock (gate)
        {
            members.Clear();

            if (incoming is not null)
            {
                members.AddRange(incoming.Select(m => new User(m.ConnectionId, m.Nickname)));
            }

            // The list is authoritative about which room it describes, including the empty list that
            // says "nobody is anywhere": without the else, a list for the room just left — still in
            // flight when the leave was sent — would put this side back inside it.
            currentRoomName = string.IsNullOrWhiteSpace(roomName) ? null : roomName;
        }

        MembersChanged?.Invoke(this, EventArgs.Empty);
    }
}
