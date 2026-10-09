using Lantern.Core;
using Lantern.Domain;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T4.5: a host with several people on at once. Everything here runs over the loopback transport with a
/// real <see cref="ChatServer"/>, real sockets and real frames — the difference between one friend and a
/// room full of them is entirely in Lantern's own code (sessions, member lists, fan-out), and that is what
/// this file is about. What needs two laptops is in <c>docs/testing/m4-field-test.md</c>.
/// </summary>
public sealed class MultiClientHostTests
{
    [Fact]
    public async Task FourPeopleAllSeeTheSameFourPeople()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await using var ines = new ChatClient("Ines");
        var crowd = new[] { cara, dov, ines };

        await JoinAllAsync(server, crowd, "Room 1");

        // Every one of them has to arrive at the same list, including the host's own window: F12 is a
        // promise about what people see, and three people seeing three different crowds is the bug.
        await Watch.UntilAsync(
            () => crowd.All(c => c.Members.Count == 4),
            "all three clients to see four members");

        var expected = new[] { "Cara", "Dov", "Hosty", "Ines" };

        foreach (var client in crowd)
        {
            Assert.Equal(expected, Names(client.Members));
        }

        Assert.Equal(expected, Names(server.RoomNamed("Room 1")!.Members));
        Assert.Equal("Room 1", server.RoomOf(host)?.Name);
    }

    [Fact]
    public async Task ANewcomerHearsTheCrowdAndTheCrowdHearsTheNewcomer()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        var caraMessages = WatchMessages(cara);
        var dovMessages = WatchMessages(dov);

        // The room is already talking when the third chair is added.
        await using var ines = new ChatClient("Ines");
        await ines.ConnectAsync(server.Endpoint);
        await ines.JoinByNameWhenShownAsync("Room 1");

        await Watch.UntilAsync(() => cara.Members.Count == 4 && dov.Members.Count == 4 && ines.Members.Count == 4,
            "four chairs in everyone's list");

        var announced = (SystemMessage)await caraMessages.ItemAsync(m => m is SystemMessage, "Ines's join notice");
        Assert.Contains("Ines", announced.Body, StringComparison.Ordinal);
        Assert.Contains("join", announced.Body, StringComparison.OrdinalIgnoreCase);

        await dovMessages.ItemAsync(m => m is SystemMessage, "the same notice for Dov");

        // The newcomer is told who was already there, not left to guess from a bare room.
        Assert.Equal(["Cara", "Dov", "Hosty", "Ines"], Names(ines.Members));
    }

    [Fact]
    public async Task OnePersonSpeakingIsHeardOnceByEveryoneElseInTheRoom()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");
        var hostFrames = WatchFrames(host);

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await using var ines = new ChatClient("Ines");
        await JoinAllAsync(server, [cara, dov, ines], "Room 1");

        var caraMessages = WatchMessages(cara);
        var dovMessages = WatchMessages(dov);
        var inesMessages = WatchMessages(ines);

        await cara.SendAsync(new TextMessage("Cara", "Room 1", "one sentence, three listeners"));

        await Watch.UntilAsync(
            () => dovMessages.CountMatch(m => m is TextMessage) == 1
                && inesMessages.CountMatch(m => m is TextMessage) == 1
                && hostFrames.CountMatch(f => f.Type == FrameType.TEXT) == 1,
            "all three listeners to get the sentence");

        // One copy each, not one per listener-route, and the speaker is not echoed to itself.
        await Task.Delay(150);
        Assert.Equal(1, dovMessages.CountMatch(m => m is TextMessage));
        Assert.Equal(1, inesMessages.CountMatch(m => m is TextMessage));
        Assert.Equal(1, hostFrames.CountMatch(f => f.Type == FrameType.TEXT));
        Assert.Equal(0, caraMessages.CountMatch(m => m is TextMessage));

        Assert.Equal(
            new[] { "one sentence, three listeners", "one sentence, three listeners", "Cara" },
            new[]
            {
                ((TextMessage)dovMessages.Items.Single(m => m is TextMessage)).Body,
                ((TextMessage)inesMessages.Items.Single(m => m is TextMessage)).Body,
                hostFrames.Items.Single(f => f.Type == FrameType.TEXT).Header.Nickname,
            });
    }

    [Fact]
    public async Task ThreeRoomsAndNobodySeesAnyoneTheyShouldNot()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");
        var hostFrames = WatchFrames(host);

        await using var inOne = new ChatClient("One");
        await using var inTwo = new ChatClient("Two");
        await using var inThree = new ChatClient("Three");

        await inOne.ConnectAsync(server.Endpoint);
        await inTwo.ConnectAsync(server.Endpoint);
        await inThree.ConnectAsync(server.Endpoint);

        await inOne.JoinByNameWhenShownAsync("Room 1");
        await inTwo.JoinByNameWhenShownAsync("Room 2");
        await inThree.JoinByNameWhenShownAsync("Room 3");

        await Watch.UntilAsync(
            () => inOne.Members.Count == 2 && inTwo.Members.Count == 1 && inThree.Members.Count == 1,
            "each room to hold its own people");

        var oneMessages = WatchMessages(inOne);
        var twoMessages = WatchMessages(inTwo);
        var threeMessages = WatchMessages(inThree);

        await inOne.SendAsync(new TextMessage("One", "Room 1", "this one is for the room I am in"));

        await Watch.UntilAsync(() => hostFrames.CountMatch(f => f.Type == FrameType.TEXT) == 1, "the roommate to hear it");

        await Task.Delay(150);
        Assert.Equal(0, twoMessages.CountMatch(m => m is TextMessage));
        Assert.Equal(0, threeMessages.CountMatch(m => m is TextMessage));

        // Not even back to the speaker, and not into the two empty rooms either.
        Assert.Equal(0, oneMessages.CountMatch(m => m is TextMessage));

        // And the member lists are still each room's own, after the traffic.
        Assert.Equal(["Hosty", "One"], Names(inOne.Members));
        Assert.Equal(["Two"], Names(inTwo.Members));
        Assert.Equal(["Three"], Names(inThree.Members));
    }

    [Fact]
    public async Task EveryoneTalkingAtOnceIsHeardByEveryoneElseOnceEach()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");
        var hostFrames = WatchFrames(host);

        var speakers = new[] { new ChatClient("A"), new ChatClient("B"), new ChatClient("C") };

        try
        {
            await JoinAllAsync(server, speakers, "Room 1");

            var messages = speakers.ToDictionary(s => s, _ => new Watch<Message>());

            foreach (var (client, watch) in messages)
            {
                client.MessageReceived += (_, message) => watch.Add(message);
            }

            // All three go at the same moment: the fan-out is not allowed to lean on one at a time.
            await Task.WhenAll(speakers.Select((s, i) => s.SendAsync(new TextMessage(s.Nickname, "Room 1", $"line {i} from {s.Nickname}"))));

            await Watch.UntilAsync(
                () => speakers.All(s => messages[s].CountMatch(m => m is TextMessage) == 2),
                "each speaker to hear the other two");
            await Watch.UntilAsync(
                () => hostFrames.CountMatch(f => f.Type == FrameType.TEXT) == 3,
                "the host to hear all three");

            await Task.Delay(150);

            foreach (var speaker in speakers)
            {
                var heard = messages[speaker].Items.OfType<TextMessage>().ToArray();

                // Two, not three: nobody is echoed to itself.
                Assert.Equal(2, heard.Length);
                Assert.DoesNotContain(speaker.Nickname, heard.Select(h => h.SenderNickname));

                // Intact, not interleaved with another speaker's bytes mid-sentence.
                foreach (var line in heard)
                {
                    Assert.StartsWith("line ", line.Body, StringComparison.Ordinal);
                    Assert.Contains(line.SenderNickname, line.Body, StringComparison.Ordinal);
                }
            }

            // Three distinct messages, not one sent twice and one lost.
            var ids = hostFrames.Items
                .Where(f => f.Type == FrameType.TEXT)
                .Select(f => f.Header.Id)
                .ToArray();

            Assert.Equal(3, ids.Distinct().Count());
        }
        finally
        {
            foreach (var speaker in speakers)
            {
                await speaker.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task EightPeopleArrivingAtOnceAllSeeNine()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        var crowd = Enumerable.Range(0, 8).Select(i => new ChatClient($"P{i}")).ToArray();

        try
        {
            // Eight at once, on eight sockets, into one room. A single-friend implementation tends to
            // survive this only by accident.
            await Task.WhenAll(crowd.Select(async client =>
            {
                await client.ConnectAsync(server.Endpoint);
                await client.JoinByNameWhenShownAsync("Room 1");
            }));

            await Watch.UntilAsync(() => crowd.All(c => c.Members.Count == 9), "everyone to see nine people");

            Assert.Equal(9, server.RoomNamed("Room 1")!.Members.Count);
            // The first to arrive and the last see the same crowd.
            Assert.Equal(Names(crowd[0].Members), Names(crowd[7].Members));
        }
        finally
        {
            foreach (var client in crowd)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task SomeoneVanishingLeavesTheRightCrowdBehind()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        var ines = new ChatClient("Ines");
        await ines.ConnectAsync(server.Endpoint);
        await ines.JoinByNameWhenShownAsync("Room 1");
        await Watch.UntilAsync(() => cara.Members.Count == 4, "the fourth to arrive");

        var caraMessages = WatchMessages(cara);
        var dovMessages = WatchMessages(dov);

        // Not a graceful leave: the connection goes, which is what a dead battery looks like.
        await ines.DisposeAsync();

        await Watch.UntilAsync(() => cara.Members.Count == 3 && dov.Members.Count == 3, "the crowd to shrink");

        Assert.Equal(["Cara", "Dov", "Hosty"], Names(cara.Members));
        Assert.Equal(["Cara", "Dov", "Hosty"], Names(dov.Members));
        Assert.Equal(3, server.RoomNamed("Room 1")!.Members.Count);

        var notice = (SystemMessage)await caraMessages.ItemAsync(m => m is SystemMessage, "the notice that someone went");
        Assert.Contains("Ines", notice.Body, StringComparison.Ordinal);
        Assert.Contains("left", notice.Body, StringComparison.OrdinalIgnoreCase);
        await dovMessages.ItemAsync(m => m is SystemMessage, "the same notice for the other roommate");

        Assert.True(cara.IsConnected && dov.IsConnected);   // one going does not take the others with it
    }

    [Fact]
    public async Task ClosingARoomWithThreePeopleInItTellsAllThree()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await using var ines = new ChatClient("Ines");
        await JoinAllAsync(server, [cara, dov, ines], "Room 1");

        var notices = new[] { cara, dov, ines }.ToDictionary(
            client => client,
            client => WatchMessages(client));

        Assert.True(server.TryDeleteRoom(server.Id("Room 1"), out _));

        foreach (var (client, watch) in notices)
        {
            // Not just any notice: the joins are still arriving when the room is closed, and one of
            // those is somebody else's name.
            var notice = (SystemMessage)await watch.ItemAsync(
                m => m is SystemMessage s && s.Body.Contains("closed", StringComparison.OrdinalIgnoreCase),
                $"{client.Nickname}'s closing notice");
            Assert.Contains("Room 1", notice.RoomName, StringComparison.Ordinal);

            // The room goes from their list as well, or they keep clicking on a room that is not there.
            await Watch.UntilAsync(
                () => !client.Rooms.Any(r => r.Name == "Room 1"),
                $"{client.Nickname} to lose the room");
            Assert.Null(client.CurrentRoomName);
        }

        // The room is gone from the chat, not merely emptied: there is nothing to click on any more.
        Assert.Null(server.RoomNamed("Room 1"));
    }

    [Fact]
    public async Task TwoPeopleWithTheSameNicknameAreStillTwoPeople()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var alex = new ChatClient("Alex");
        await using var alsoAlex = new ChatClient("Alex");
        await JoinAllAsync(server, [alex, alsoAlex], "Room 1");

        await Watch.UntilAsync(() => alex.Members.Count == 3, "both Alexes and the host");

        var crowd = server.RoomNamed("Room 1")!.Members;

        // Two people who both called themselves Alex, so they are kept apart by who they are connected
        // as rather than by what they are called. A room whose list drops one of them has lost a person.
        Assert.Equal(3, crowd.Count);
        Assert.Equal(2, crowd.Count(u => u.Nickname == "Alex"));
        Assert.Equal(3, crowd.Select(u => u.ConnectionId).Distinct().Count());

        var hostFrames = WatchFrames(host);
        var heardByTheOtherAlex = WatchMessages(alsoAlex);

        await alex.SendAsync(new TextMessage("Alex", "Room 1", "one of us said this"));

        var arrived = await hostFrames.ItemAsync(f => f.Type == FrameType.TEXT, "the said thing");
        Assert.Equal("Alex", arrived.Header.Nickname);
        Assert.Equal("one of us said this", arrived.Header.Text);

        // The other Alex is a listener like any other: the server keeps members apart by who is
        // connected, so sharing a name does not make one of them the speaker.
        var heard = await heardByTheOtherAlex.ItemAsync(m => m is TextMessage, "the other Alex to hear it");
        Assert.Equal("one of us said this", ((TextMessage)heard).Body);
        Assert.Equal(3, alsoAlex.Members.Count);
    }

    // ---------------------------------------------------------------- plumbing

    private static async Task<ChatServer> StartAsync()
    {
        var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host");
        await server.StartAsync();
        return server;
    }

    private static async Task<LocalParticipant> HostJoinsAsync(ChatServer server, string roomName)
    {
        var local = new LocalParticipant("Hosty");
        Assert.True(server.Register(local));
        server.JoinByName(local, roomName);
        await Task.CompletedTask;
        return local;
    }

    private static async Task JoinAllAsync(ChatServer server, IEnumerable<ChatClient> clients, string roomName)
    {
        foreach (var client in clients)
        {
            await client.ConnectAsync(server.Endpoint);
            await client.JoinByNameWhenShownAsync(roomName);
        }

        await Watch.UntilAsync(
            () => clients.All(c => c.CurrentRoomName == roomName),
            $"the joins to reach {roomName}");
    }

    private static Watch<Frame> WatchFrames(LocalParticipant participant)
    {
        var watch = new Watch<Frame>();
        participant.FrameReceived += (_, frame) => watch.Add(frame);
        return watch;
    }

    private static Watch<Message> WatchMessages(ChatClient client)
    {
        var watch = new Watch<Message>();
        client.MessageReceived += (_, message) => watch.Add(message);
        return watch;
    }

    /// <summary>Nicknames, sorted, so a member list is compared without depending on join order.</summary>
    private static string[] Names(IEnumerable<User> members)
        => members.Select(u => u.Nickname).OrderBy(n => n, StringComparer.Ordinal).ToArray();
}
