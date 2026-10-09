using System.Windows.Forms;
using Lantern.Core;
using Lantern.Domain;

namespace Lantern.Tests;

/// <summary>
/// A test talks about rooms by name, because that is what a person says and what a window shows. A service
/// asks for a room by its identity, because the name is the thing a host may be changing.
/// <para>
/// These helpers are the translation between the two, and they are the only place in the test project where a
/// name is used to mean a room. A test that needs the identity itself asks for it out loud with
/// <see cref="IdOf(IChatService, string)"/>, which is what it is: a lookup, done in the open, of the room
/// that happens to answer to that name in this test's list.
/// </para>
/// </summary>
internal static class TestRooms
{
    /// <summary>Joins the room this list currently calls <paramref name="name"/>; null means join nothing.</summary>
    public static Task JoinByNameAsync(this IChatService service, string? name, CancellationToken cancel = default)
        => service.JoinRoomAsync(string.IsNullOrWhiteSpace(name) ? null : IdOf(service.Rooms, name!), cancel);

    /// <summary>
    /// Joins the room this client has been told is called <paramref name="name"/>, waiting for the list to
    /// arrive first. A window can only enter a room it has been shown, and a client that has only this second
    /// connected has been shown nothing yet - so a test that says "join Room 1" about a connected client is
    /// describing a row that has to turn up before it can be clicked.
    /// </summary>
    public static async Task JoinByNameWhenShownAsync(this ChatClient client, string name, CancellationToken cancel = default)
    {
        await Watch.UntilAsync(
            () => client.Rooms.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)),
            $"{client.Nickname} to be told about the room \"{name}\"");

        await client.JoinRoomAsync(client.Rooms.IdOf(name), cancel);
    }

    public static Task RenameByNameAsync(this IChatService service, string currentName, string newName, CancellationToken cancel = default)
        => service.RenameRoomAsync(IdOf(service.Rooms, currentName), newName, cancel);

    public static Task DeleteByNameAsync(this IChatService service, string name, CancellationToken cancel = default)
        => service.DeleteRoomAsync(IdOf(service.Rooms, name), cancel);

    /// <summary>The same, for a test that walks up to the engine itself instead of going through a service.</summary>
    public static void JoinByName(this ChatServer server, IParticipant participant, string? name)
        => server.Join(participant, name is null ? null : IdOf(server.Rooms, name));

    public static Guid IdOf(this IChatService service, string name) => IdOf(service.Rooms, name);

    /// <summary>
    /// What the room list says, in order. A row is a room, and this reads a row the only way a person can - by
    /// what it says - which is also the only fair way for a test to compare it with a name it wrote itself.
    /// </summary>
    public static IEnumerable<string> Names(this ListBox.ObjectCollection rows)
        => rows.Cast<object>().Select(row => row?.ToString() ?? string.Empty);

    /// <summary>Clicks the row saying this name.</summary>
    public static void SelectByName(this ListBox rooms, string name)
        => rooms.SelectedIndex = Enumerable.Range(0, rooms.Items.Count)
            .First(i => string.Equals(rooms.Items[i]?.ToString(), name, StringComparison.Ordinal));

    /// <summary>
    /// The room the engine calls this name right now, or null. A test reads the engine the way a person reads
    /// a list - by what it says - and this is the lookup that turns that back into the room itself.
    /// </summary>
    public static Room? RoomNamed(this ChatServer server, string name)
        => server.Rooms.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The same, for a test that cannot go on without the room. A raw frame about a room has to say which room
    /// it means, and on the wire that is an identity rather than a name.
    /// </summary>
    public static Room RoomOrThrow(this ChatServer server, string name)
        => server.RoomNamed(name) ?? throw new InvalidOperationException($"The engine has no room named \"{name}\".");

    public static Guid Id(this ChatServer server, string name) => server.RoomOrThrow(name).Id;

    public static Guid IdOf(this ChatServer server, string name) => IdOf(server.Rooms, name);

    /// <summary>
    /// Which room answers to this name right now. Named <c>IdOf</c> rather than something that sounds like a
    /// lookup of a room, so it never quietly becomes the way a test refers to a room a second time.
    /// </summary>
    public static Guid IdOf(this IEnumerable<Room> rooms, string name)
        => rooms.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))?.Id
           ?? throw new InvalidOperationException($"No room named \"{name}\" in this test's room list.");
}
