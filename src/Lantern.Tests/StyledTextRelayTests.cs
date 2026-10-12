using Lantern.Core;
using Lantern.Domain;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T12.1 on a live link: a formatted line has to survive everything between one window and the next.
/// <para>
/// The wire tests in <see cref="StyledTextTests"/> can only prove a frame carries its runs. Between two
/// windows there is a host that does not forward a client's frame at all — it rebuilds the message under its
/// own stamp and sends that out again — and a host window with no frame in sight at all. Rebuild a message
/// from its sentence and forget the annotation, and the formatting stops dead on the host's own machine.
/// </para>
/// </summary>
public sealed class StyledTextRelayTests
{
    [Fact]
    public async Task TheHostSpeaksFormattedAndTheClientHearsItFormatted()
    {
        var (server, host) = await StartAsync();
        await using (server)
        await using (host)
        {
            await host.JoinByNameAsync("Room 1");

            await using var client = new ChatClient("Cara");
            var received = WatchMessages(client);
            await client.ConnectAsync(server.Endpoint);
            await client.JoinByNameWhenShownAsync("Room 1");
            await Watch.UntilAsync(() => client.Members.Count == 2, "both members in the list");

            IReadOnlyList<StyledRun> runs = [new StyledRun(14), new StyledRun(7, Bold: true)];

            await host.SendAsync(new TextMessage(host.Nickname, "Room 1", "make yourself at home") { Styles = runs });

            var line = (TextMessage)await received.ItemAsync(m => m is TextMessage, "the host's line");

            Assert.Equal("make yourself at home", line.Body);
            Assert.Equal(runs, line.Styles);
        }
    }

    /// <summary>
    /// The relay is the dangerous one, because the host answers a TEXT frame by making a new
    /// <see cref="TextMessage"/> out of the words in it and broadcasting that. Here the runs have to come
    /// through the frame, past the engine, and back out into another frame — and the sentence has to arrive
    /// as the same sentence it was, since that is the half both machines are entitled to agree on.
    /// </summary>
    [Fact]
    public async Task AStyledLineCrossesTheHostStillStyled()
    {
        var (server, host) = await StartAsync();
        await using (server)
        await using (host)
        {
            await host.JoinByNameAsync("Room 1");

            await using var dov = new ChatClient("Dov");
            await using var ines = new ChatClient("Ines");
            var heard = WatchMessages(ines);

            await dov.ConnectAsync(server.Endpoint);
            await ines.ConnectAsync(server.Endpoint);
            await dov.JoinByNameWhenShownAsync("Room 1");
            await ines.JoinByNameWhenShownAsync("Room 1");
            await Watch.UntilAsync(() => dov.Members.Count == 3 && ines.Members.Count == 3, "all three in the list");

            IReadOnlyList<StyledRun> runs = [new StyledRun(9), new StyledRun(6, Italic: true), new StyledRun(6)];

            await dov.SendAsync(new TextMessage("Dov", "Room 1", "mind the italic word!") { Styles = runs });

            var line = (TextMessage)await heard.ItemAsync(m => m is TextMessage, "Ines to hear from Dov");

            Assert.Equal("mind the italic word!", line.Body);
            Assert.Equal("Dov", line.SenderNickname);
            Assert.Equal(runs, line.Styles);
        }
    }

    /// <summary>
    /// Refusal travels the same road. A peer that puts runs on the wire which do not describe their own
    /// sentence reaches the room as the plain message it always was: the words, the sender, nobody the wiser.
    /// </summary>
    [Fact]
    public async Task AStyledLineThatDoesNotAddUpReachesTheRoomAsWords()
    {
        var (server, host) = await StartAsync();
        await using (server)
        await using (host)
        {
            await host.JoinByNameAsync("Room 1");

            await using var ears = new ChatClient("Ears");
            var heard = WatchMessages(ears);
            await ears.ConnectAsync(server.Endpoint);
            await ears.JoinByNameWhenShownAsync("Room 1");
            await Watch.UntilAsync(() => ears.Members.Count == 2, "Ears in the room");

            // A peer with no client behind it, so nothing on this side judges the runs before the wire does.
            using var rogue = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
            await SendRawAsync(rogue, Frame.Hello("Rogue"));
            await SendRawAsync(rogue, Frame.JoinRoom(server.Id("Room 1")));

            await SendRawAsync(rogue, new Frame(FrameHeader.FromJson(
                """{"t":"TEXT","nick":"Rogue","room":"Room 1","text":"still readable","styles":[{"n":3,"b":true},{"n":99}]}""")));

            var line = (TextMessage)await heard.ItemAsync(m => m is TextMessage, "Ears to hear from Rogue");

            Assert.Equal("still readable", line.Body);
            Assert.Equal("Rogue", line.SenderNickname);
            Assert.Empty(line.Styles);
        }
    }

    /// <summary>
    /// And the host's window is not exempt from the rule it enforces on everyone else. Runs that do not add
    /// up are dropped before the host files its own copy, so this is the test that keeps one room from
    /// containing two accounts of one message: the host sees plain words, and so does the client.
    /// </summary>
    [Fact]
    public async Task AStyledLineThatDoesNotAddUpIsPlainInTheHostsOwnWindowToo()
    {
        var (server, host) = await StartAsync();
        await using (server)
        await using (host)
        {
            await host.JoinByNameAsync("Room 1");

            await using var client = new ChatClient("Cara");
            var received = WatchMessages(client);
            await client.ConnectAsync(server.Endpoint);
            await client.JoinByNameWhenShownAsync("Room 1");
            await Watch.UntilAsync(() => client.Members.Count == 2, "both members in the list");

            var message = new TextMessage(host.Nickname, "Room 1", "one line, two accounts") { Styles = [new StyledRun(99, Bold: true)] };
            await host.SendAsync(message);

            var heard = (TextMessage)await received.ItemAsync(m => m is TextMessage, "the client's copy");
            Assert.Empty(heard.Styles);

            var room = host.Rooms.Single(r => r.Name == "Room 1");
            await Watch.UntilAsync(
                () => room.Transcript.OfType<TextMessage>().Any(m => m.Id == message.Id),
                "the host's own copy of the line");

            var kept = room.Transcript.OfType<TextMessage>().Single(m => m.Id == message.Id);

            Assert.Equal(message.Id, kept.Id);
            Assert.Equal("one line, two accounts", kept.Body);
            Assert.Empty(kept.Styles);
        }
    }

    // ----------------------------------------------------------------------- machinery

    private static async Task<(ChatServer Server, HostChatService Service)> StartAsync()
    {
        var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host");
        await server.StartAsync();

        var service = new HostChatService(server, "Hosty");
        await service.StartAsync();

        return (server, service);
    }

    private static Watch<Message> WatchMessages(IChatService client)
    {
        var watch = new Watch<Message>();
        client.MessageReceived += (_, message) => watch.Add(message);
        return watch;
    }

    private static async Task SendRawAsync(IClientLink link, Frame frame)
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await FrameCodec.WriteAsync(link.Output, frame, cancel.Token);
    }
}
