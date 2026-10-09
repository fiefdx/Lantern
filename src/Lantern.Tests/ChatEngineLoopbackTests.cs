using Lantern.Core;
using Lantern.Domain;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T2.4 and T2.5: the chat engine over the loopback transport. This is the whole product minus
/// Bluetooth and minus windows — a real <see cref="ChatServer"/>, real sockets, real frames — so the
/// scenarios in the specification (1, 2, 3, 6) are proven here rather than by clicking.
/// </summary>
public sealed class ChatEngineLoopbackTests
{
    // ------------------------------------------------------------------ hello

    [Fact]
    public async Task AConnectedClientSeesTheRoomsTheHostOpened()
    {
        await using var server = await StartAsync();

        await using var client = new ChatClient("Cara");
        await client.ConnectAsync(server.Endpoint);

        Assert.False(client.IsHost);
        Assert.True(client.IsConnected);
        Assert.Equal(["Room 1", "Room 2", "Room 3"], client.Rooms.Select(r => r.Name));
    }

    [Fact]
    public async Task AClientConnectingToNothingIsToldRatherThanHanging()
    {
        await using var client = new ChatClient("Cara");
        var missing = new ServerEndpoint("ghost", LoopbackPorts.Address(LoopbackPorts.BasePort + LoopbackPorts.Range - 1), LanternProtocol.ServiceId);

        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync(missing));
        Assert.False(client.IsConnected);
    }

    // -------------------------------------------------------------- membership

    [Fact]
    public async Task JoiningARoomPutsBothSidesInItWithTheSameMemberList()
    {
        await using var server = await StartAsync();
        var local = await HostJoinsAsync(server, "Room 1");

        await using var client = new ChatClient("Cara");
        await client.ConnectAsync(server.Endpoint);
        await client.JoinByNameWhenShownAsync("Room 1");

        await Watch.UntilAsync(() => client.Members.Count == 2, "the client to see both members");

        Assert.Equal(2, server.RoomNamed("Room 1")!.Members.Count);
        Assert.Equal(["Cara", "Hosty"], client.Members.Select(u => u.Nickname).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal("Room 1", client.CurrentRoomName);
        Assert.NotNull(server.RoomOf(local));
    }

    [Fact]
    public async Task JoinAndLeaveAreAnnouncedToTheRoom()
    {
        await using var server = await StartAsync();
        var local = await HostJoinsAsync(server, "Room 1");
        var frames = WatchFrames(local);

        await using (var client = new ChatClient("Cara"))
        {
            await client.ConnectAsync(server.Endpoint);
            await client.JoinByNameWhenShownAsync("Room 1");

            var join = await frames.ItemAsync(
                f => f.Type == FrameType.SYSTEM_NOTICE && f.Header.Text!.Contains("join"),
                "the join notice");
            Assert.Equal("Room 1", join.Header.Room);
            Assert.Contains("Cara", join.Header.Text);
        }

        // Scenario 3: whoever is left behind hears about the departure too.
        var leave = await frames.ItemAsync(
            f => f.Type == FrameType.SYSTEM_NOTICE && f.Header.Text!.Contains("left"),
            "the leave notice");
        Assert.Contains("Cara", leave.Header.Text);

        await Watch.UntilAsync(RoomHasOnlyTheHost, "the member list to shrink");

        bool RoomHasOnlyTheHost() => server.RoomNamed("Room 1")!.Members.Count == 1;
        Assert.Equal(["Hosty"], server.RoomNamed("Room 1")!.Members.Select(u => u.Nickname));
    }

    [Fact]
    public async Task NobodySitsInTwoRoomsAtOnce()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var client = new ChatClient("Cara");
        await client.ConnectAsync(server.Endpoint);
        await client.JoinByNameWhenShownAsync("Room 1");
        await Watch.UntilAsync(() => server.RoomNamed("Room 1")!.Members.Count == 2, "the join");

        await client.JoinByNameWhenShownAsync("Room 2");

        await Watch.UntilAsync(() => server.RoomNamed("Room 1")!.Members.Count == 1, "Room 1 to be emptied");
        Assert.Equal(["Hosty"], server.RoomNamed("Room 1")!.Members.Select(u => u.Nickname));
        Assert.Equal(["Cara"], server.RoomNamed("Room 2")!.Members.Select(u => u.Nickname));
    }

    [Fact]
    public async Task AMemberCanLeaveWithoutDisconnecting()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var client = new ChatClient("Cara");
        await client.ConnectAsync(server.Endpoint);
        await client.JoinByNameWhenShownAsync("Room 1");
        await Watch.UntilAsync(() => client.Members.Count == 2, "the client to see the room it joined");

        await client.JoinByNameAsync(null);

        // The host's room shrinks first; the frame telling the client is still in flight, so wait for
        // the client's own view instead of asserting on state that has not arrived yet.
        await Watch.UntilAsync(
            () => client.CurrentRoomName is null && client.Members.Count == 0,
            "the client to see itself out of the room");

        Assert.True(client.IsConnected);
    }

    // ------------------------------------------------------------- text (F6)

    [Fact]
    public async Task TextTravelsFromOneMemberToTheOtherAndIsNotEchoedBack()
    {
        await using var server = await StartAsync();
        var local = await HostJoinsAsync(server, "Room 1");
        var hostFrames = WatchFrames(local);

        await using var client = new ChatClient("Cara");
        var clientMessages = WatchMessages(client);
        await client.ConnectAsync(server.Endpoint);
        await client.JoinByNameWhenShownAsync("Room 1");
        await Watch.UntilAsync(() => client.Members.Count == 2, "both members in the list");

        await client.SendAsync(new TextMessage("Cara", "Room 1", "hello from the client"));

        var arrived = await hostFrames.ItemAsync(f => f.Type == FrameType.TEXT, "the text frame");
        Assert.Equal("hello from the client", arrived.Header.Text);
        Assert.Equal("Cara", arrived.Header.Nickname);

        // Scenario 1 on the other side: the host's own window speaks like any other member.
        server.SendText(local, "and hello from the host");

        var echoed = await clientMessages.ItemAsync(m => m is TextMessage, "the client to receive the host's line");
        Assert.Equal("and hello from the host", ((TextMessage)echoed).Body);
        Assert.Equal("Hosty", echoed.SenderNickname);

        Assert.Equal(1, clientMessages.CountMatch(m => m is TextMessage));
    }

    [Fact]
    public async Task MembersOfDifferentRoomsDoNotSeeEachOther()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 2");      // the host's window listens in Room 2
        var hostFrames = WatchFrames(host);

        await using var inRoomOne = new ChatClient("One");
        await using var inRoomTwo = new ChatClient("Two");
        var oneMessages = WatchMessages(inRoomOne);
        var twoMessages = WatchMessages(inRoomTwo);

        await inRoomOne.ConnectAsync(server.Endpoint);
        await inRoomTwo.ConnectAsync(server.Endpoint);
        await inRoomOne.JoinByNameWhenShownAsync("Room 1");
        await inRoomTwo.JoinByNameWhenShownAsync("Room 2");
        await Watch.UntilAsync(
            () => server.RoomNamed("Room 2")!.Members.Count == 2 && server.RoomNamed("Room 1")!.Members.Count == 1,
            "both joins");

        await inRoomTwo.SendAsync(new TextMessage("Two", "Room 2", "only Room 2 should hear this"));

        // Room 2 hears it. The sender is not echoed to itself, so Room 2's own client stays quiet too.
        await Watch.UntilAsync(() => hostFrames.CountMatch(f => f.Type == FrameType.TEXT) == 1, "Room 2 to get the line");

        // Give anything misrouted the chance to show up before declaring Room 1 clean.
        await Task.Delay(150);
        Assert.Equal(0, oneMessages.CountMatch(m => m is TextMessage));
        Assert.Equal(0, twoMessages.CountMatch(m => m is TextMessage));
    }

    [Fact]
    public async Task FiftyMessagesArriveInOrder()
    {
        await using var server = await StartAsync();
        var local = await HostJoinsAsync(server, "Room 1");
        var hostFrames = WatchFrames(local);

        await using var client = new ChatClient("Cara");
        await client.ConnectAsync(server.Endpoint);
        await client.JoinByNameWhenShownAsync("Room 1");
        await Watch.UntilAsync(() => client.Members.Count == 2, "the join");

        for (var i = 0; i < 50; i++)
        {
            await client.SendAsync(new TextMessage("Cara", "Room 1", $"line {i}"));
        }

        await Watch.UntilAsync(() => hostFrames.CountMatch(f => f.Type == FrameType.TEXT) == 50, "all fifty frames");

        var bodies = hostFrames.Items.Where(f => f.Type == FrameType.TEXT).Select(f => f.Header.Text).ToArray();
        Assert.Equal(Enumerable.Range(0, 50).Select(i => $"line {i}"), bodies);
    }

    // -------------------------------------------------------------- refusals

    [Fact]
    public async Task AClientCannotEditRooms()
    {
        await using var server = await StartAsync();
        await using var client = new ChatClient("Cara");
        await client.ConnectAsync(server.Endpoint);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateRoomAsync("Sneaky"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RenameByNameAsync("Room 1", "Sneaky"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteByNameAsync("Room 1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartAsync());

        Assert.DoesNotContain("Sneaky", server.Rooms.Select(r => r.Name));
    }

    [Fact]
    public async Task AMessageTooLongIsRefusedAndSaysWhy()
    {
        await using var server = await StartAsync();
        var local = await HostJoinsAsync(server, "Room 1");
        var hostFrames = WatchFrames(local);

        await using var client = new ChatClient("Cara");
        await client.ConnectAsync(server.Endpoint);
        await client.JoinByNameWhenShownAsync("Room 1");
        await Watch.UntilAsync(() => client.Members.Count == 2, "the honest client to be in the room");

        // Refused on this side before anything is written, so the window can say why (F10).
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(new TextMessage("Cara", "Room 1", new string('x', ValidationRules.MessageMaxLength + 1))));

        // The same refusal arrives when a peer ignores the rule and puts it on the wire anyway.
        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Mallory"));
        await SendRawAsync(raw, Frame.JoinRoom(server.Id("Room 1")));
        await SendRawAsync(raw, Frame.Text(Guid.NewGuid(), server.RoomOrThrow("Room 1"), "Mallory", new string('y', ValidationRules.MessageMaxLength + 1), DateTime.Now));

        var refusal = await ReadRawAsync(raw, FrameType.ERROR);
        Assert.Contains("characters", refusal.Header.Error);

        // Give a refused message the chance to reach the room anyway; it must not.
        await Task.Delay(150);
        Assert.Equal(0, hostFrames.CountMatch(f => f.Type == FrameType.TEXT));
    }

    [Fact]
    public async Task SpeakingBeforeJoiningARoomIsRefused()
    {
        await using var server = await StartAsync();
        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);

        await SendRawAsync(raw, Frame.Hello("Mallory"));
        await SendRawAsync(raw, Frame.Text(Guid.NewGuid(), server.RoomOrThrow("Room 1"), "Mallory", "anyone there?", DateTime.Now));

        var error = await ReadRawAsync(raw, FrameType.ERROR);
        Assert.Contains("room", error.Header.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JoiningARoomThatDoesNotExistIsRefusedAndTheListIsResent()
    {
        await using var server = await StartAsync();
        await using var client = new ChatClient("Cara");
        var problems = new Watch<string>();
        await client.ConnectAsync(server.Endpoint);
        client.ServerError += (_, text) => problems.Add(text);

        // A room that does not exist is an identity that is not on the list, which is what a window asking
        // for a row it never had amounts to now. The host answers in words, and re-sends the list so that the
        // window cannot go on showing a room it is not in.
        await client.JoinRoomAsync(Guid.NewGuid());

        var problem = await problems.ItemAsync(t => t.Contains("room"), "the refusal");
        Assert.Contains("not here any more", problem);
        Assert.Null(client.CurrentRoomName);

        // The client's list is re-sent so the window cannot stay showing a room that isn't there.
        Assert.Equal(["Room 1", "Room 2", "Room 3"], client.Rooms.Select(r => r.Name));
    }

    /// <summary>
    /// What is left to refuse: the frames only a host may send. A client that announces a room list is
    /// either confused or pretending to be something else, and either way it gets a sentence rather than
    /// a silence.
    /// <para>
    /// Everything else that used to be refused here has gone on the wire since: pictures in T5.2 (see
    /// <c>PictureMessageTests</c>) and files in T5.3 (see <c>FileTransferTests</c>). That is the milestone
    /// statement worth keeping in the test file - there came a point at which this build was carrying all
    /// twelve frames it claims to, and a frame it does not carry is answered rather than dropped.
    /// </para>
    /// </summary>
    [Fact]
    public async Task FramesOnlyAHostMaySendAreRefused()
    {
        await using var server = await StartAsync();
        await using var client = new ChatClient("Cara");
        await client.ConnectAsync(server.Endpoint);
        await client.JoinByNameWhenShownAsync("Room 1");

        // A file is a transfer, not a message: a FileMessage says a file is coming and has no way to send
        // it, so this is a mistake about what a message is.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(new FileMessage("Cara", "Room 1", "notes.zip", 12)));

        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Mallory"));
        await SendRawAsync(raw, Frame.RoomList([new Room("Room 1")]));

        var error = await ReadRawAsync(raw, FrameType.ERROR);
        Assert.Contains("ROOM_LIST", error.Header.Error);
    }

    [Fact]
    public async Task AFrameClaimingSomeoneElsesNameIsNotBelieved()
    {
        await using var server = await StartAsync();
        var local = await HostJoinsAsync(server, "Room 1");
        var hostFrames = WatchFrames(local);

        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Mallory"));
        await JoinRawAsync(raw, server.RoomOrThrow("Room 1"));
        await SendRawAsync(raw, Frame.Text(Guid.NewGuid(), server.RoomOrThrow("Room 1"), "Someone Else", "I am not me", DateTime.Now));

        var arrived = await hostFrames.ItemAsync(f => f.Type == FrameType.TEXT, "the text frame");

        // The server stamps the identity it verified in HELLO, so a client cannot put words in
        // another person's mouth.
        Assert.Equal("Mallory", arrived.Header.Nickname);
    }

    [Fact]
    public async Task ASecondHelloOnOneConnectionIsRefused()
    {
        await using var server = await StartAsync();
        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);

        await SendRawAsync(raw, Frame.Hello("Mallory"));
        await ReadRawAsync(raw, FrameType.HELLO_ACK);
        await SendRawAsync(raw, Frame.Hello("Someone Else"));

        var error = await ReadRawAsync(raw, FrameType.ERROR);
        Assert.Equal(["Mallory"], server.Participants.Select(p => p.Nickname));
    }

    // ------------------------------------------------------- the host's rooms

    [Fact]
    public async Task EveryClientFollowsTheHostsRoomListLive()
    {
        await using var server = await StartAsync();
        await using var client = new ChatClient("Cara");
        var roomChanges = new Watch<EventArgs>();
        await client.ConnectAsync(server.Endpoint);
        client.RoomsChanged += (_, e) => roomChanges.Add(e);

        Assert.True(server.TryCreateRoom("Lobby", out var created));
        Assert.Null(created);
        await Watch.UntilAsync(() => client.Rooms.Any(r => r.Name == "Lobby"), "the new room to arrive");

        Assert.True(server.TryRenameRoom(server.Id("Lobby"), "Lounge", out var renamed));
        Assert.Null(renamed);
        await Watch.UntilAsync(() => client.Rooms.Any(r => r.Name == "Lounge"), "the rename to arrive");

        Assert.True(server.TryDeleteRoom(server.Id("Room 3"), out var deleted));
        Assert.Null(deleted);
        await Watch.UntilAsync(() => !client.Rooms.Any(r => r.Name == "Room 3"), "the deletion to arrive");

        Assert.Equal(["Room 1", "Room 2", "Lounge"], client.Rooms.Select(r => r.Name));
        Assert.NotEmpty(roomChanges.Items);
    }

    /// <summary>
    /// Renaming a room changes what it is called and nothing else. The people inside stay inside; the room each
    /// of them is looking at stays the room they are standing in; and the next thing anybody says still lands
    /// where they were already standing.
    /// <para>
    /// Rooms used to be found by name, on both machines at once, so a rename moved the room out from under
    /// everybody in it: their windows kept showing the transcript they had been reading, while the engine had a
    /// different object with a different name and nobody's membership. This is the test that says that room is
    /// still the room.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ARenamedRoomKeepsThePeopleWhoWereInIt()
    {
        await using var server = await StartAsync();
        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");

        await cara.ConnectAsync(server.Endpoint);
        await cara.JoinByNameWhenShownAsync("Room 2");
        await dov.ConnectAsync(server.Endpoint);
        await dov.JoinByNameWhenShownAsync("Room 2");

        var roomId = server.Id("Room 2");
        Assert.Equal("Room 2", cara.CurrentRoomName);

        Assert.True(server.TryRenameRoom(roomId, "Quiet Corner", out var error));
        Assert.Null(error);

        // Both windows are still in a room, and it is the renamed one - not in nothing, and not in the room
        // whose old name they happen to still be holding.
        await Watch.UntilAsync(
            () => cara.CurrentRoomName == "Quiet Corner" && dov.CurrentRoomName == "Quiet Corner",
            "both windows to follow the rename");

        Assert.Equal(roomId, cara.CurrentRoomId);
        Assert.Contains("Cara", server.RoomNamed("Quiet Corner")!.Members.Select(u => u.Nickname));
        Assert.Contains("Dov", server.RoomNamed("Quiet Corner")!.Members.Select(u => u.Nickname));

        // And the room is still reachable by the people standing in it, which is what a room is for.
        var heard = WatchMessages(dov);
        await cara.SendAsync(new TextMessage("Cara", "Quiet Corner", "still here"));
        await heard.ItemAsync(
            m => m is TextMessage text && text.Body == "still here",
            "the message to reach the renamed room");
    }

    /// <summary>
    /// The name a message carries is content, not an address: it says what the room was called when the line
    /// was written. Rewriting the room's name must not rewrite history, and must not make the line undeliverable
    /// either - which is why the routing above goes by identity and the text keeps saying the old name.
    /// </summary>
    [Fact]
    public async Task ALinesRoomNameIsHistoryRatherThanAnAddress()
    {
        await using var server = await StartAsync();
        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");

        await cara.ConnectAsync(server.Endpoint);
        await cara.JoinByNameWhenShownAsync("Room 2");
        await dov.ConnectAsync(server.Endpoint);
        await dov.JoinByNameWhenShownAsync("Room 2");

        var heard = WatchMessages(dov);
        await cara.SendAsync(new TextMessage("Cara", "Room 2", "before the rename"));
        var line = await heard.ItemAsync(m => m is TextMessage text && text.Body == "before the rename", "the message");

        Assert.Equal("Room 2", line.RoomName);

        Assert.True(server.TryRenameRoom(server.Id("Room 2"), "Quiet Corner", out _));
        await Watch.UntilAsync(() => dov.CurrentRoomName == "Quiet Corner", "the window to follow the rename");

        // The line the window already has still says where it was written. That is a fact about the past and
        // not an address: rewriting it would rewrite what somebody read, and the room it names no longer
        // answers to that name - which is exactly why nothing routes by it any more.
        Assert.Equal("Room 2", line.RoomName);
    }

    [Fact]
    public async Task TheHostCannotHaveTwoRoomsWithTheSameName()
    {
        await using var server = await StartAsync();

        Assert.False(server.TryCreateRoom("Room 1", out var duplicate));
        Assert.Contains("already exists", duplicate);

        Assert.False(server.TryCreateRoom("   ", out var blank));
        Assert.NotNull(blank);

        Assert.False(server.TryCreateRoom(new string('r', ValidationRules.RoomNameMaxLength + 1), out var tooLong));
        Assert.NotNull(tooLong);
    }

    [Fact]
    public async Task DeletingARoomTellsThePeopleInsideAndPutsThemNowhere()
    {
        await using var server = await StartAsync();
        await using var client = new ChatClient("Cara");
        var messages = WatchMessages(client);
        await client.ConnectAsync(server.Endpoint);
        await client.JoinByNameWhenShownAsync("Room 2");
        await Watch.UntilAsync(() => server.RoomNamed("Room 2")!.Members.Count == 1, "the join");

        Assert.True(server.TryDeleteRoom(server.Id("Room 2"), out _));

        var notice = (SystemMessage)await messages.ItemAsync(
            m => m is SystemMessage s && s.Body.Contains("closed by the host"),
            "the eviction notice");

        await Watch.UntilAsync(() => client.CurrentRoomName is null, "the client to end up in no room");
        Assert.Empty(client.Members);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task ARenameKeepsWhoeverWasInside()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 2");

        await using var client = new ChatClient("Cara");
        await client.ConnectAsync(server.Endpoint);
        await client.JoinByNameWhenShownAsync("Room 2");
        await Watch.UntilAsync(() => server.RoomNamed("Room 2")!.Members.Count == 2, "both members");

        Assert.True(server.TryRenameRoom(server.Id("Room 2"), "Quiet Corner", out _));

        var renamed = server.RoomNamed("Quiet Corner");
        Assert.NotNull(renamed);
        Assert.Equal(2, renamed.Members.Count);
        Assert.Null(server.RoomNamed("Room 2"));
    }

    // ----------------------------------------------------- connection loss (6)

    [Fact]
    public async Task WhenTheServerStopsTheClientNotices()
    {
        await using var server = await StartAsync();
        await using var client = new ChatClient("Cara");
        var lost = new Watch<Exception>();
        await client.ConnectAsync(server.Endpoint);
        await client.JoinByNameWhenShownAsync("Room 1");
        client.ConnectionLost += (_, reason) => lost.Add(reason);

        await server.StopAsync();

        var reason = await lost.ItemAsync(_ => true, "the client to notice the host vanished");
        Assert.False(string.IsNullOrWhiteSpace(reason.Message));
        Assert.False(client.IsConnected);
        Assert.Empty(client.Members);

        // Scenario 6: the window offers to go back to the startup screen, so a client that keeps
        // talking has to be refused rather than quietly buffering.
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(new TextMessage("Cara", "Room 1", "can anyone hear me?")));
    }

    [Fact]
    public async Task WhenAClientVanishesTheHostStillShowsOneMember()
    {
        await using var server = await StartAsync();
        var local = await HostJoinsAsync(server, "Room 1");
        var hostFrames = WatchFrames(local);

        var client = new ChatClient("Cara");
        await client.ConnectAsync(server.Endpoint);
        await client.JoinByNameWhenShownAsync("Room 1");
        await Watch.UntilAsync(() => server.RoomNamed("Room 1")!.Members.Count == 2, "the join");

        // Abrupt on purpose: there is no goodbye frame, the host learns from the socket closing.
        await client.DisposeAsync();

        var leave = await hostFrames.ItemAsync(f => f.Type == FrameType.SYSTEM_NOTICE && f.Header.Text!.Contains("left"), "the leave notice");
        Assert.Contains("Cara", leave.Header.Text);
        await Watch.UntilAsync(() => server.ConnectedCount == 1, "the session to be dropped");
        Assert.Equal(["Hosty"], server.RoomNamed("Room 1")!.Members.Select(u => u.Nickname));
    }

    [Fact]
    public async Task AConnectionThatNeverIntroducesItselfIsDroppedWithoutANotice()
    {
        await using var server = await StartAsync();
        var local = await HostJoinsAsync(server, "Room 1");
        var hostFrames = WatchFrames(local);
        var probes = new Watch<string>();
        server.ProbeRejected += (_, who) => probes.Add(who);

        // What BtLoopbackClient.DiscoverAsync does when it scans for hosts: dial, say nothing, hang up.
        var probe = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        probe.Close();

        await Watch.UntilAsync(() => probes.CountMatch(_ => true) == 1, "the probe to be rejected");
        await Task.Delay(100);

        Assert.Equal(0, hostFrames.CountMatch(f => f.Type == FrameType.SYSTEM_NOTICE));
        Assert.Equal(1, server.ConnectedCount);
    }

    // ------------------------------------------------------ a window that throws

    [Fact]
    public async Task AWindowThatThrowsWhileListeningDoesNotEndTheConnection()
    {
        await using var server = await StartAsync();
        var local = await HostJoinsAsync(server, "Room 1");

        await using var client = new ChatClient("Cara");
        await client.ConnectAsync(server.Endpoint);
        await client.JoinByNameWhenShownAsync("Room 1");
        await Watch.UntilAsync(() => client.Members.Count == 2, "both members in the list");

        var heard = new Watch<string>();

        client.MessageReceived += (_, message) =>
        {
            if (message.Preview.Contains("the bad one"))
            {
                // What a real window does when it is being closed: throw on the way out of the event.
                // The delivery happens on the client's read loop, so this exception used to end the
                // connection quietly - "connected", and nothing else ever arriving.
                throw new InvalidOperationException("the window's handle has gone away");
            }

            heard.Add(message.Preview);
        };

        server.SendText(local, "the bad one");
        server.SendText(local, "and the next one");

        Assert.Equal("and the next one", await heard.ItemAsync(_ => true, "the message after the throw"));
        Assert.True(client.IsConnected);
        Assert.Equal(2, client.Members.Count);
        Assert.Contains(server.Participants, p => p.Nickname == "Cara");   // still a member of the chat
    }

    // ---------------------------------------------------------------- plumbing

    private static async Task<ChatServer> StartAsync()
    {
        var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host");
        await server.StartAsync();
        return server;
    }

    /// <summary>Opens the host's own window as a member, which is how the host takes part (T2.7).</summary>
    private static async Task<LocalParticipant> HostJoinsAsync(ChatServer server, string roomName)
    {
        var local = new LocalParticipant("Hosty");
        Assert.True(server.Register(local));
        server.JoinByName(local, roomName);
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

    private static async Task SendRawAsync(IClientLink link, Frame frame)
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await FrameCodec.WriteAsync(link.Output, frame, cancel.Token);
    }

    /// <summary>
    /// Reads until a frame of <paramref name="wanted"/> arrives, skipping the HELLO_ACK and ROOM_LIST a
    /// host sends without being asked. Fails the test rather than hanging the run.
    /// </summary>
    private static async Task<Frame> ReadRawAsync(IClientLink link, FrameType wanted)
    {
        var skipped = new List<FrameType>();

        while (true)
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            Frame? frame;

            try
            {
                frame = await FrameCodec.ReadAsync(link.Input, cancel.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"no {wanted} frame arrived; saw {string.Join(", ", skipped)}");
            }

            Assert.NotNull(frame);

            if (frame!.Type == wanted)
            {
                return frame;
            }

            skipped.Add(frame.Type);
        }
    }

    private static Task JoinRawAsync(IClientLink link, Room room)
        => SendRawAsync(link, Frame.JoinRoom(room.Id));
}
