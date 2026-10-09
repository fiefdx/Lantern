using System.Net.Sockets;
using Lantern.Core;
using Lantern.Domain;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// The use-case scenarios from the document, run against the real engine: a <see cref="ChatServer"/>
/// on loopback TCP, a <see cref="HostChatService"/> standing in for the host's window, and
/// <see cref="ChatClient"/> instances standing in for other people's windows - built the way
/// <c>StartupForm</c> builds them, with discovery in between.
/// <para>
/// These are the slowest tests in the suite because they cross real sockets, and they are the only
/// ones that exercise the whole path from one window's keystroke to another window's transcript.
/// </para>
/// <para>
/// Note the barriers: <c>JoinRoomAsync</c> returning only means the request was written, so every
/// action here that depends on somebody else having arrived waits for that arrival first. Without
/// them the suite has a window in which the host speaks to a room the other person has asked to join
/// but has not been put in yet - and the server correctly delivers the line to nobody.
/// </para>
/// </summary>
public sealed class EndToEndScenarioTests
{
    private const string HostName = "Kitchen Table";

    /// <summary>The host's own nickname, kept separate from the name of the chat it is hosting.</summary>
    private const string HostNickname = "Hosty";

    // ---------------------------------------------------------------- scenario 1 and 2

    [Fact]
    public async Task Scenario1And2AHostStartsAChatAndAClientFindsAndJoinsIt()
    {
        await using var host = await HostAChatAsync();

        // Scenario 1: the host announces and waits. Nothing has joined yet.
        Assert.True(host.Server.IsRunning);
        Assert.Equal(HostNickname, host.Host.Nickname);

        // Scenario 2: the other person's window shows what it found, by name and by address (F11).
        var found = await ServerBrowser.DiscoverAsync();
        var advertised = Assert.Single(found, e => e.DeviceAddress == host.Server.Endpoint.DeviceAddress);

        // Discovery can only report an address: nobody has said who lives there yet, so the name is a
        // placeholder built from the port. The real name arrives with HELLO_ACK (T2.6).
        Assert.Equal($"Lantern on port {LoopbackPorts.BasePort}", advertised.ServerName);
        Assert.Contains($"{LoopbackPorts.BasePort}", advertised.DisplayName);

        await using var cara = await JoinAsync("Cara", host.Server.Endpoint);

        Assert.True(cara.IsConnected);
        Assert.Equal(HostName, cara.Endpoint!.ServerName);      // the handshake is where the name comes from
        Assert.Null(cara.CurrentRoomName);                    // connected, but standing in no room
        await Watch.UntilAsync(() => cara.Rooms.Count == 3, "the list of rooms to arrive");
        Assert.Equal(
            new[] { "Room 1", "Room 2", "Room 3" },
            cara.Rooms.Select(r => r.Name));                  // the three default rooms (F10)

        var fromHost = WatchMessages(host.Host);

        await cara.JoinByNameWhenShownAsync("Room 1");

        // A member list only ever comes from the server, so seeing herself in it is proof the join landed.
        await Watch.UntilAsync(() => cara.Members.Count == 1, "the server to put Cara in the room");

        Assert.Equal("Room 1", cara.CurrentRoomName);
        Assert.Equal(["Cara"], RoomNames(cara));              // the host is in no room, so Cara is alone
        Assert.Empty(host.Host.Members);                      // and the host's window lists nothing

        // F5 announces a join *in the room*, and the host is not in it, so nothing about Cara reaches the
        // host's window. ChatForm would filter such a message out of the transcript anyway (F4): a window
        // shows one conversation, not the whole chat.
        await Task.Delay(200);   // a bounded quiet period: what has not arrived by now is not arriving
        Assert.DoesNotContain(fromHost.Items, m => m.Preview.Contains("Cara"));
    }

    // ---------------------------------------------------------------- scenario 4: text chat

    [Fact]
    public async Task Scenario4TheHostAndTwoClientsHoldATextConversationInOneRoom()
    {
        await using var host = await HostAChatAsync();
        await host.Host.JoinByNameAsync("Room 1");

        await using var cara = await JoinAsync("Cara", host.Server.Endpoint);
        await cara.JoinByNameWhenShownAsync("Room 1");

        await using var davide = await JoinAsync("Davide", host.Server.Endpoint);
        await davide.JoinByNameWhenShownAsync("Room 1");

        // Nobody speaks until all three windows agree on who is in the room.
        await EveryoneSeesEveryone(host.Host, cara, davide);

        var fromCara = WatchMessages(cara);
        var fromDavide = WatchMessages(davide);
        var fromHost = WatchMessages(host.Host);

        await host.Host.SendAsync(Line(host.Host, "Room 1", "I can see you both"));
        await cara.SendAsync(Line(cara, "Room 1", "loud and clear"));
        await davide.SendAsync(Line(davide, "Room 1", "welcome to my machine"));

        await Watch.UntilAsync(() => fromCara.CountMatch(m => m is TextMessage) == 2, "Cara to hear the other two");
        await Watch.UntilAsync(() => fromDavide.CountMatch(m => m is TextMessage) == 2, "Davide to hear the other two");
        await Watch.UntilAsync(() => fromHost.CountMatch(m => m is TextMessage) == 2, "the host to hear the other two");

        // A window keeps its own line rather than being sent it back (F4), so what each of them received
        // is exactly what the other two wrote.
        // <para>
        // The order they arrived in is deliberately not asserted. One connection's messages do keep their
        // order - see TwoRoomsTalkToNobodyButTheirOwnMembers - but two people speaking are two connections,
        // each with its own queue on the host, and Lantern promises nobody's sentences will be shuffled
        // rather than that the room agrees on the order two strangers happened to type. A test that pinned
        // that down would be a test that occasionally fails for a reason no one can fix.
        // </para>
        Assert.Equal(
            ["I can see you both", "welcome to my machine"],
            fromCara.Items.OfType<TextMessage>().Select(m => m.Preview).OrderBy(p => p, StringComparer.Ordinal));

        Assert.Equal(
            ["I can see you both", "loud and clear"],
            fromDavide.Items.OfType<TextMessage>().Select(m => m.Preview).OrderBy(p => p, StringComparer.Ordinal));

        // Every line carries the nickname of the window that wrote it, not of the one showing it (F4).
        Assert.Equal(
            ["Cara", "Davide"],
            fromHost.Items.OfType<TextMessage>().Select(m => m.SenderNickname).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Scenario4EveryWindowHoldsTheSameConversationHistory()
    {
        await using var host = await HostAChatAsync();
        await host.Host.JoinByNameAsync("Room 2");

        await using var cara = await JoinAsync("Cara", host.Server.Endpoint);
        var fromCara = WatchMessages(cara);      // subscribed before joining, so her own notice is seen
        await cara.JoinByNameWhenShownAsync("Room 2");
        await EveryoneSeesEveryone(host.Host, cara);

        await host.Host.SendAsync(Line(host.Host, "Room 2", "the host spoke"));
        await cara.SendAsync(Line(cara, "Room 2", "and so did I"));
        await Watch.UntilAsync(
            () => Transcript(cara, "Room 2").Length == 2 && Transcript(host.Host, "Room 2").Length == 2,
            "both windows to hold both lines");

        // Leaving and re-entering replays the room's transcript (F6), so what each window holds is the
        // conversation, message for message. The id travels too: two windows agreeing on the words but
        // not on the messages would be a bug that a preview-only comparison would miss.
        Assert.Equal(
            Transcript(host.Host, "Room 2").Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal),
            Transcript(cara, "Room 2").Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal));

        // The order is not the same, and is not supposed to be: a window puts the line you sent down the
        // moment you send it and the line from elsewhere when it arrives, so two people typing at the
        // same moment see their own line first. Sorting by the sender's clock would not fix it, because
        // the two machines do not agree what time it is.

        Assert.Equal(["Cara - join this chat"], fromCara.Items.OfType<SystemMessage>().Select(m => m.Preview));

        // What is not compared is the notices: each window only witnesses the comings and goings that
        // happened while it was standing in the room. The host saw its own join before Cara arrived,
        // and Cara never saw that one - which is right, and would be a wrong thing to assert equal.
    }

    // ---------------------------------------------------------------- scenario 3: leaving

    [Fact]
    public async Task Scenario3LeavingARoomIsAnnouncedAndEveryListFollows()
    {
        await using var host = await HostAChatAsync();
        await host.Host.JoinByNameAsync("Room 2");

        await using var davide = await JoinAsync("Davide", host.Server.Endpoint);
        await davide.JoinByNameWhenShownAsync("Room 2");
        await EveryoneSeesEveryone(host.Host, davide);

        var fromDavide = WatchMessages(davide);
        var fromHost = WatchMessages(host.Host);

        await davide.JoinByNameAsync(null);          // Room → (none) on the window

        await Watch.UntilAsync(() => fromHost.Items.Any(m => m.Preview.Contains("left")), "the host to hear the leaving");
        await Watch.UntilAsync(() => host.Host.Members.Count == 1, "the host's list to drop him");

        // The room is told that someone left; the person who left is not, because they are not in the
        // room to be told anything about - their own window simply empties (F5).
        Assert.Equal(1, fromHost.CountMatch(m => m.Preview.Contains("left")));
        Assert.Equal(0, fromDavide.CountMatch(m => m.Preview.Contains("left")));
        Assert.Equal([HostNickname], RoomNames(host.Host));
        Assert.Null(davide.CurrentRoomName);
        Assert.Empty(davide.Members);
        Assert.True(davide.IsConnected);           // leaving a room is not leaving the chat
    }

    [Fact]
    public async Task Scenario3DisconnectingIsTreatedTheSameAsLeaving()
    {
        await using var host = await HostAChatAsync();
        await host.Host.JoinByNameAsync("Room 1");

        var cara = await JoinAsync("Cara", host.Server.Endpoint);
        await cara.JoinByNameWhenShownAsync("Room 1");
        await EveryoneSeesEveryone(host.Host, cara);

        var fromHost = WatchMessages(host.Host);

        // Closing a window without saying goodbye (F12): the connection simply goes away.
        await cara.DisposeAsync();

        await Watch.UntilAsync(() => fromHost.Items.Any(m => m.Preview.Contains("left")), "the host to notice");
        await Watch.UntilAsync(() => host.Host.Members.Count == 1, "the list to shrink");

        Assert.Equal(1, fromHost.CountMatch(m => m.Preview.Contains("left")));
        Assert.Equal([HostNickname], RoomNames(host.Host));
        Assert.DoesNotContain("Cara", host.Server.Participants.Select(p => p.Nickname));
    }

    [Fact]
    public async Task Scenario6WhenTheHostStopsTheClientsAreToldOnce()
    {
        await using var host = await HostAChatAsync();

        await using var cara = await JoinAsync("Cara", host.Server.Endpoint);
        await cara.JoinByNameWhenShownAsync("Room 3");
        await Watch.UntilAsync(() => cara.Members.Count == 1, "Cara to be in the room on her own");

        var lost = new Watch<string>();
        cara.ConnectionLost += (_, ex) => lost.Add(ex.Message);

        await host.Host.ShutdownAsync();           // File → Quit on the host

        var reason = await lost.ItemAsync(_ => true, "the client to be told it lost the host");
        Assert.False(cara.IsConnected);
        Assert.DoesNotContain("host-local", host.Server.Participants.Select(p => p.ConnectionId));

        // One notice, not one per outstanding message, and the transcripts survive: what the user wrote
        // is still on their screen even though nothing more can arrive.
        Assert.Single(lost.Items);   // one notice, not one per outstanding frame
        Assert.NotEmpty(reason);
        Assert.NotEmpty(cara.Rooms);
    }

    // ---------------------------------------------------------------- rooms keep people apart

    [Fact]
    public async Task TwoRoomsTalkToNobodyButTheirOwnMembers()
    {
        await using var host = await HostAChatAsync();
        await host.Host.JoinByNameAsync("Room 1");

        await using var cara = await JoinAsync("Cara", host.Server.Endpoint);
        await cara.JoinByNameWhenShownAsync("Room 1");
        await EveryoneSeesEveryone(host.Host, cara);

        await using var davide = await JoinAsync("Davide", host.Server.Endpoint);
        await davide.JoinByNameWhenShownAsync("Room 2");      // a different room of the same chat
        await Watch.UntilAsync(() => davide.Members.Count == 1, "Davide to be alone in Room 2");

        var fromHost = WatchMessages(host.Host);
        var fromCara = WatchMessages(cara);
        var fromDavide = WatchMessages(davide);

        await cara.SendAsync(Line(cara, "Room 1", "in room one"));
        await Watch.UntilAsync(() => fromHost.CountMatch(m => m is TextMessage) == 1, "the host to hear Room 1");

        await davide.SendAsync(Line(davide, "Room 2", "this one is private"));
        await cara.SendAsync(Line(cara, "Room 1", "and again in room one"));

        // Receiving the second Room 1 line proves the host's connection is still delivering; if the
        // private line arrived later it would have arrived before that one, since the server answers
        // each room on its own.
        await Watch.UntilAsync(() => fromHost.CountMatch(m => m is TextMessage) == 2, "the host to hear the next Room 1 line");

        Assert.Equal(
            ["in room one", "and again in room one"],
            fromHost.Items.OfType<TextMessage>().Select(m => m.Preview));

        // Cara wrote both lines herself, so they were never sent to her; Davide heard nothing of Room 1.
        Assert.Empty(fromCara.Items.OfType<TextMessage>());
        Assert.Empty(fromDavide.Items.OfType<TextMessage>());
        Assert.Equal(0, fromHost.CountMatch(m => m.Preview.Contains("private")));
        Assert.Equal(0, fromCara.CountMatch(m => m.Preview.Contains("private")));

        // Davide's window lists the room he is in, which is his alone; Room 1 still lists two.
        Assert.Equal("Room 2", davide.CurrentRoomName);
        Assert.Equal(["Davide"], RoomNames(davide));
        Assert.Equal(2, host.Host.Members.Count);
    }

    [Fact]
    public async Task TheHostClosingARoomTellsThePeopleInsideAndTheyEndUpNowhere()
    {
        await using var host = await HostAChatAsync();

        await using var cara = await JoinAsync("Cara", host.Server.Endpoint);
        await cara.JoinByNameWhenShownAsync("Room 3");
        await Watch.UntilAsync(() => cara.Members.Count == 1, "Cara to be in Room 3 on her own");

        var fromCara = WatchMessages(cara);

        await host.Host.DeleteByNameAsync("Room 3");

        await Watch.UntilAsync(
            () => fromCara.Items.Any(m => m.Preview.Contains("closed by the host")),
            "the eviction notice");

        Assert.DoesNotContain("Room 3", cara.Rooms.Select(r => r.Name));
        Assert.Null(cara.CurrentRoomName);           // nowhere to stand, but still connected
        Assert.Empty(cara.Members);
        Assert.True(cara.IsConnected);
    }

    // -------------------------------------------------------------- plumbing

    /// <summary>One message as a window would build it.</summary>
    private static TextMessage Line(IChatService sender, string roomName, string body)
        => new(sender.Nickname, roomName, body);

    /// <param name="hostName">What the chat is called; the host's nickname is separate - see below.</param>
    private static async Task<HostChat> HostAChatAsync(string hostName = HostName)
    {
        // Exactly StartupForm's host branch: transport, server, then a window service for the host.
        var server = new ChatServer(new BtLoopbackServer(hostName), hostName);
        await server.StartAsync();

        // A machine called "Kitchen Table" is not a person: the name of the chat and the nickname of
        // the person hosting it are different things, and only the nickname is capped at 16 characters
        // (F1). The first draft of this file conflated them and the validator caught it.
        var host = new HostChatService(server, HostNickname);
        await host.StartAsync();

        return new HostChat(server, host);
    }

    /// <summary>
    /// A client the way the startup window builds one: scan, pick what answered, connect. Proves the
    /// discovered address is actually dialable rather than merely listed.
    /// </summary>
    private static async Task<ChatClient> JoinAsync(string nickname, ServerEndpoint endpoint)
    {
        var found = await ServerBrowser.DiscoverAsync();
        var advertised = found.Single(e => e.DeviceAddress == endpoint.DeviceAddress);

        var client = new ChatClient(nickname);
        await client.ConnectAsync(advertised);
        return client;
    }

    /// <summary>
    /// Waits until every window lists every nickname - the barrier that makes the tests above honest,
    /// because a join that has been written is not a join that has been applied.
    /// </summary>
    private static Task EveryoneSeesEveryone(params IChatService[] windows)
    {
        var wanted = windows.Select(w => w.Nickname).ToArray();

        return Watch.UntilAsync(
            () => windows.All(w => wanted.All(n => w.Members.Any(u => u.Nickname == n))),
            $"every window to list {string.Join(", ", wanted)}");
    }

    private static Watch<Message> WatchMessages(IChatService service)
    {
        var watch = new Watch<Message>();
        service.MessageReceived += (_, message) => watch.Add(message);
        return watch;
    }

    private static string[] RoomNames(IChatService service)
        => service.Members.Select(u => u.Nickname).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// The lines a room remembers, oldest first, text only: the id is part of it because two windows
    /// that agree on the words but not on the messages have a bug.
    /// </summary>
    private static (string Id, string Preview)[] Transcript(IChatService service, string roomName)
    {
        var room = service.Rooms.Single(r => r.Name == roomName);
        return [.. room.Transcript.Where(m => m is TextMessage).Select(m => (m.Id.ToString("N"), m.Preview))];
    }

    /// <summary>The host process: a server plus the window service that sits in it.</summary>
    private sealed record HostChat(ChatServer Server, HostChatService Host) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await Server.DisposeAsync();
        }
    }
}
