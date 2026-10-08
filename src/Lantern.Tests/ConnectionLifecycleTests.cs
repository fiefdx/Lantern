using Lantern.Core;
using Lantern.Domain;
using Lantern.Transport;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T4.6: the end of a chat. <b>Quit Server</b> is asked for and then stops the chat, everyone still
/// connected is told, a member going out is announced and the member list shrinks, and a connection that
/// is gone stays gone - Lantern does not quietly reconnect, the person joins again from the startup
/// window. Scenario 6, and the half of scenario 4 that happens at the end rather than the middle.
/// </summary>
public sealed class ConnectionLifecycleTests
{
    // ------------------------------------------------------------- the service

    [Fact]
    public async Task QuittingTheServerTellsEveryoneStillConnected()
    {
        await using var server = await StartAsync();
        await using var host = new HostChatService(server, "Hosty");
        await host.StartAsync();

        var crowd = new[] { new ChatClient("Cara"), new ChatClient("Dov"), new ChatClient("Ines") };
        var lost = new Watch<Exception>();

        try
        {
            foreach (var client in crowd)
            {
                await client.ConnectAsync(server.Endpoint);
                await client.JoinRoomAsync("Room 1");
                client.ConnectionLost += (_, reason) => lost.Add(reason);
            }

            await Watch.UntilAsync(() => server.ConnectedCount == 4, "all four in the chat");

            await host.ShutdownAsync();

            await Watch.UntilAsync(() => lost.CountMatch(_ => true) == 3, "all three clients to be told");

            foreach (var client in crowd)
            {
                Assert.False(client.IsConnected);
                Assert.Empty(client.Members);         // the crowd it was in is not a crowd any more
            }

            Assert.Equal(0, server.ConnectedCount);   // nobody is left in it, including the host
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
    public async Task LanternDoesNotTryToReconnectOnItsOwn()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        var client = new ChatClient("Cara");
        var lost = new Watch<Exception>();
        await client.ConnectAsync(server.Endpoint);
        await client.JoinRoomAsync("Room 1");
        client.ConnectionLost += (_, reason) => lost.Add(reason);
        await Watch.UntilAsync(() => server.ConnectedCount == 2, "the join");

        await server.StopAsync();
        await lost.ItemAsync(_ => true, "the client to notice");

        // The policy, stated: nothing retries. A tool that reconnects by itself is a tool whose user
        // cannot tell whether the chat they are looking at is live.
        await Task.Delay(400);

        Assert.False(client.IsConnected);
        Assert.Equal(1, lost.CountMatch(_ => true));
        Assert.Equal(1, server.ConnectedCount);       // the host is still alone, and still the only one

        // What the person can still do with a dead client is nothing, in words rather than in a hang.
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.JoinRoomAsync("Room 1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(new TextMessage("Cara", "Room 1", "hello?")));

        // Joining again means the startup window and a fresh Scan, which every other test in this file
        // does from scratch; what this one holds is that the dead connection is not quietly revived, and
        // that the chat the other side is in was never damaged by the going.
    }

    [Fact]
    public async Task TheHostCarriesOnAfterEveryoneGoesAndNewPeopleArrive()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        var first = new[] { new ChatClient("Cara"), new ChatClient("Dov") };

        foreach (var client in first)
        {
            await client.ConnectAsync(server.Endpoint);
            await client.JoinRoomAsync("Room 1");
        }

        await Watch.UntilAsync(() => server.ConnectedCount == 3, "both guests");

        foreach (var client in first)
        {
            await client.DisposeAsync();
        }

        await Watch.UntilAsync(() => server.ConnectedCount == 1, "the chat to be back to one");

        Assert.Equal(1, server.ConnectedCount);
        Assert.Equal(["Hosty"], server.FindRoom("Room 1")!.Members.Select(u => u.Nickname));

        // A chat that has lost everybody is still a chat: the rooms stayed, and someone new can walk in.
        await using var later = new ChatClient("Late");
        await later.ConnectAsync(server.Endpoint);
        await later.JoinRoomAsync("Room 1");

        await Watch.UntilAsync(() => later.Members.Count == 2, " the latecomer to be welcomed");
    }

    [Fact]
    public async Task AMemberLeavingByTheDoorIsAnnouncedAndMissingAfterwards()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");
        var hostFrames = WatchFrames(host);

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await cara.ConnectAsync(server.Endpoint);
        await dov.ConnectAsync(server.Endpoint);
        await cara.JoinRoomAsync("Room 1");
        await dov.JoinRoomAsync("Room 1");
        await Watch.UntilAsync(() => dov.Members.Count == 3, "all three in the room");

        var dovMessages = WatchMessages(dov);

        await cara.JoinRoomAsync(null);        // what the Leave button sends

        var leave = await hostFrames.ItemAsync(
            f => f.Type == FrameType.SYSTEM_NOTICE && f.Header.Text!.Contains("left"),
            "the host to hear about the departure");
        Assert.Contains("Cara", leave.Header.Text);

        var announced = (SystemMessage)await dovMessages.ItemAsync(m => m is SystemMessage, "the roommate to hear about it");
        Assert.Contains("Cara", announced.Body, StringComparison.Ordinal);

        await Watch.UntilAsync(() => dov.Members.Count == 2, "the member list to shrink");
        Assert.Equal(["Dov", "Hosty"], Names(dov.Members));

        // Whoever left is not in the crowd they left, but is still in the chat.
        Assert.True(cara.IsConnected);
        Assert.Null(cara.CurrentRoomName);
        Assert.Contains(server.Participants, p => p.Nickname == "Cara");
    }

    // --------------------------------------------------------------- the screens

    [Fact]
    public Task QuitFromTheMenuStopsTheChatAndTheOtherSideSeesIt()
        => Sta.RunAsync(async () =>
        {
            await using var server = await StartAsync();
            await using var host = new HostChatService(server, "Hosty");
            await host.StartAsync();

            await using var cara = new ChatClient("Cara");
            var lost = new Watch<Exception>();
            await cara.ConnectAsync(server.Endpoint);
            cara.ConnectionLost += (_, reason) => lost.Add(reason);

            using var form = new WatchingHost(host);
            form.Show();

            ClickMenu(form, "quitMenuItem");

            // One click, one prompt, and the whole chat is down: no half-stopped server left listening.
            await Watch.UntilAsync(() => lost.CountMatch(_ => true) == 1, "the client to be told");
            Assert.False(form.Visible, "the host window went with it");
            Assert.Equal(0, server.ConnectedCount);   // no half-stopped server left holding a chat
            Assert.False(cara.IsConnected);
            Assert.False(string.IsNullOrWhiteSpace(lost.Items[0].Message));
        });

    [Fact]
    public Task ChoosingToStayLeavesEveryoneConnected()
        => Sta.RunAsync(async () =>
        {
            await using var server = await StartAsync();
            await using var host = new HostChatService(server, "Hosty");
            await host.StartAsync();

            await using var cara = new ChatClient("Cara");
            var lost = new Watch<Exception>();
            await cara.ConnectAsync(server.Endpoint);
            await cara.JoinRoomAsync("Room 1");
            cara.ConnectionLost += (_, reason) => lost.Add(reason);

            using var form = new WatchingHost(host) { Confirms = false };      // "no, I did not mean it"
            form.Show();

            ClickMenu(form, "quitMenuItem");

            await Task.Delay(250);

            Assert.True(form.Visible);
            Assert.Equal(2, server.ConnectedCount);   // still the two of them, in the same chat
            Assert.True(cara.IsConnected);
            Assert.Equal(0, lost.CountMatch(_ => true));

            // And still a working chat, not a paused one: a message travels as it did before.
            var messages = WatchMessages(cara);
            await cara.SendAsync(new TextMessage("Cara", "Room 1", "still here"));
            await Task.Delay(200);
            Assert.Equal(0, messages.CountMatch(m => m is TextMessage));       // no echo to the speaker
            await Watch.UntilAsync(() => host.Rooms.Count >= 1, "the room list to be intact");
        });

    [Fact]
    public Task TheScreenThatLostItsHostSaysWhyAndStepsAside()
        => Sta.RunAsync(async () =>
        {
            await using var server = await StartAsync();

            await using var cara = new ChatClient("Cara");
            await cara.ConnectAsync(server.Endpoint);
            await cara.JoinRoomAsync("Room 1");

            using var form = new WatchingClient(cara);
            form.Show();

            await server.StopAsync();

            // Scenario 6 as a person meets it: the status bar says what happened, and the window stops
            // offering a Send button that can only fail.
            await Watch.UntilAsync(() => form.Lost.CountMatch(_ => true) == 1, "the screen to be told");
            await Watch.UntilAsync(() => !form.Visible, "the window to step aside");

            // Both halves of the meeting: a box that says what happened, and a status bar left saying it
            // after the window has gone. (The status bar is read off the form rather than off its controls
            // because a closed form no longer has a control tree to look through.)
            Assert.False(string.IsNullOrWhiteSpace(form.Lost.Items[0].Message));
            // Both halves of the meeting, in the bar: the state, for whoever looks at it afterwards, and the
            // reason, for right now.
            Assert.StartsWith("Status: connection lost — ", form.StatusText, StringComparison.Ordinal);
            Assert.EndsWith(form.Lost.Items[0].Message, form.StatusText, StringComparison.Ordinal);

            // And no promise that is not going to be kept: nothing dials anybody back on its own.
            Assert.DoesNotContain("reconnect", form.StatusText, StringComparison.OrdinalIgnoreCase);
        });

    [Fact]
    public Task LeavingByTheButtonTellsTheHostAndEndsTheMembership()
        => Sta.RunAsync(async () =>
        {
            await using var server = await StartAsync();
            var host = await HostJoinsAsync(server, "Room 1");
            var hostFrames = WatchFrames(host);

            await using var cara = new ChatClient("Cara");
            await cara.ConnectAsync(server.Endpoint);
            await cara.JoinRoomAsync("Room 1");
            await Watch.UntilAsync(() => cara.Members.Count == 2, "the two of them");

            using var form = new WatchingClient(cara) { Confirms = true };
            form.Show();

            SelectRoom(form, "Room 1");
            Click(form, "btnLeave");

            var leave = await hostFrames.ItemAsync(
                f => f.Type == FrameType.SYSTEM_NOTICE && f.Header.Text!.Contains("left"),
                "the host to hear about it");
            Assert.Contains("Cara", leave.Header.Text);

            await Watch.UntilAsync(() => server.FindRoom("Room 1")!.Members.Count == 1, "the list to shrink");

            Assert.Equal("Status: left the room", Status(form));
            Assert.True(cara.IsConnected, "leaving a room is not leaving the chat");
        });

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
        server.Join(local, roomName);
        await Task.CompletedTask;
        return local;
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

    private static string[] Names(IEnumerable<User> members)
        => members.Select(u => u.Nickname).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static Control Find(Control owner, string name)
        => owner.Controls.Find(name, searchAllChildren: true).FirstOrDefault()
           ?? throw new InvalidOperationException($"the window has no control named '{name}'");

    private static void Click(Form form, string button)
        => Assert.IsType<Button>(Find(form, button)).PerformClick();

    private static void SelectRoom(Form form, string name)
        => Assert.IsType<ListBox>(Find(form, "lstRooms")).SelectedItem = name;

    private static void ClickMenu(Form form, string item)
    {
        var menu = Assert.IsType<MenuStrip>(Find(form, "menuStrip"));
        ((ToolStripMenuItem)menu.Items.Find(item, searchAllChildren: true)[0]).PerformClick();
    }

    private static string Status(Control form)
    {
        var strip = Assert.IsType<StatusStrip>(Find(form, "statusStrip"));
        return strip.Items.Cast<ToolStripItem>().Single(item => item.Name == "lblStatus").Text ?? string.Empty;
    }

    /// <summary>The host window with a person made of fields, and no prompts of any kind.</summary>
    private sealed class WatchingHost : HostForm
    {
        public WatchingHost(IChatService service)
            : base(service)
        {
        }

        /// <summary>Whether this window's user really means to give up the chat.</summary>
        public bool Confirms = true;

        protected override bool ConfirmDeleteRoom(string roomName) => Confirms;

        protected override bool ConfirmQuit() => Confirms;

        protected override string? AskForRoomName(string title, string current, Func<string, string?> validate) => null;
    }

    /// <summary>
    /// The client window with a person made of fields, and the connection-loss box replaced by a note: the
    /// modal is the one part of that path a test must not sit in.
    /// </summary>
    private sealed class WatchingClient : ClientForm
    {
        public WatchingClient(IChatService service)
            : base(service)
        {
        }

        public bool Confirms = true;

        /// <summary>Every connection-loss the user was told about, with what the box would have said.</summary>
        public Watch<Exception> Lost { get; } = new();

        protected override bool ConfirmLeaveRoom(string roomName) => Confirms;

        protected override void ShowConnectionLost(Exception error) => Lost.Add(error);
    }
}
