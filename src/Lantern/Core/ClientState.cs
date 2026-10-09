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

    /// <summary>
    /// Every room this side knows, by identity. There is no lookup by name on purpose: the name is what the
    /// host may change at any moment, so a room found by name is a room that can be silently the wrong one -
    /// or two rooms, which is what used to happen on a rename.
    /// </summary>
    private readonly Dictionary<Guid, Room> roomsById = [];

    private readonly List<Room> rooms = [];
    private readonly List<User> members = [];
    private Guid? currentRoomId;

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

    /// <summary>
    /// Which room this side believes it is in, by identity. This is the thing to compare with; the name of
    /// the current room is <see cref="CurrentRoomName"/>, and that is for showing to a person.
    /// </summary>
    public Guid? CurrentRoomId
    {
        get
        {
            lock (gate)
            {
                return currentRoomId;
            }
        }
    }

    /// <summary>What the room this side is in is called right now, or null when it is in none.</summary>
    public string? CurrentRoomName => FindRoom(CurrentRoomId)?.Name;

    /// <summary>The room with this identity, if it is known.</summary>
    public Room? FindRoom(Guid? id)
    {
        if (id is null)
        {
            return null;
        }

        lock (gate)
        {
            return roomsById.GetValueOrDefault(id.Value);
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
                ApplyMemberList(frame.Header.RoomId, frame.Header.Members);
                return null;

            case FrameType.TEXT:
            case FrameType.PICTURE:
            case FrameType.FILE_META:
            case FrameType.SYSTEM_NOTICE:
                var message = frame.ToMessage();

                if (message is not null)
                {
                    // The frame says which room it belongs to by identity. Routing it by the name in it would
                    // put a line sent just before a rename into a room invented from the old name.
                    Record(message, frame.Header.RoomId ?? default);
                }

                return message;

            default:
                return null;
        }
    }

    /// <summary>
    /// Records something this side said, into the room it was said in. The server does not echo a message
    /// back to its author, so without this, re-entering a room would replay everyone else's lines and lose
    /// your own.
    /// </summary>
    public void Keep(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Record(message, CurrentRoomId ?? throw new InvalidOperationException("You are not in a room."));
    }

    /// <summary>
    /// Puts a line into the room the frame named. An unknown identity is still kept: the next ROOM_LIST
    /// decides whether that room is shown, and a message that arrived is evidence a room exists even if the
    /// list is one frame behind.
    /// </summary>
    public void Record(Message message, Guid roomId)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (roomId == Guid.Empty)
        {
            // A frame with nothing in either room field. Nothing to file it under, and inventing a room from
            // a name is how one room became two.
            return;
        }

        lock (gate)
        {
            if (!roomsById.TryGetValue(roomId, out var room))
            {
                roomsById[roomId] = room = new Room(roomId, message.RoomName);
            }

            // Adopt, not Append: the name this line quotes may be one the room stopped answering to while
            // the line was in the air.
            room.Adopt(message);
        }
    }

    /// <summary>
    /// Sets the current room as soon as the request to join leaves, so the window does not wait for the round
    /// trip to enable itself. Every reply corrects it if the host refused.
    /// </summary>
    public void ExpectRoom(Guid? roomId)
    {
        lock (gate)
        {
            currentRoomId = roomId == Guid.Empty ? null : roomId;
        }
    }

    /// <summary>Forgets the membership without forgetting the transcripts (a connection ended).</summary>
    public void ClearMembership()
    {
        lock (gate)
        {
            members.Clear();
            currentRoomId = null;
        }

        Dispatch.Safe(() => MembersChanged?.Invoke(this, EventArgs.Empty));
    }

    private void ApplyRoomList(IReadOnlyList<FrameRoom>? listed)
    {
        if (listed is null)
        {
            return;
        }

        lock (gate)
        {
            rooms.Clear();

            foreach (var entry in listed)
            {
                if (roomsById.TryGetValue(entry.Id, out var known))
                {
                    // The same room, possibly called something else now. Renamed in place - same identity,
                    // same transcript - because dropping it and making a new one is what used to throw away
                    // the history of a room the host had merely renamed.
                    var room = string.Equals(known.Name, entry.Name, StringComparison.Ordinal)
                        ? known
                        : known.RenamedTo(entry.Name);

                    roomsById[entry.Id] = room;
                    rooms.Add(room);
                    continue;
                }

                var fresh = new Room(entry.Id, entry.Name);
                roomsById[entry.Id] = fresh;
                rooms.Add(fresh);
            }

            if (currentRoomId is not null && !roomsById.ContainsKey(currentRoomId.Value))
            {
                // The host closed the room this side was standing in.
                currentRoomId = null;
            }
        }

        Dispatch.Safe(() => RoomsChanged?.Invoke(this, EventArgs.Empty));
    }

    private void ApplyMemberList(Guid? roomId, IReadOnlyList<FrameMember>? incoming)
    {
        lock (gate)
        {
            members.Clear();

            if (incoming is not null)
            {
                members.AddRange(incoming.Select(m => new User(m.ConnectionId, m.Nickname)));
            }

            // The list is authoritative about which room it describes, including the empty list that
            // says "nobody is anywhere": without the assignment, a list for the room just left - still in
            // flight when the leave was sent - would put this side back inside it.
            currentRoomId = roomId;
        }

        // Raised outside the lock, and wrapped: the window showing this state is the thing most likely
        // to throw (a form being closed, for instance), and it must not be able to stop the client that
        // is telling it about itself. See Dispatch.
        Dispatch.Safe(() => MembersChanged?.Invoke(this, EventArgs.Empty));
    }
}
