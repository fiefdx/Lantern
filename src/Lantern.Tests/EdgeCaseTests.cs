using System.Net.Sockets;
using Lantern.Core;
using Lantern.Domain;
using Lantern.Storage;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T6.1: the ways a room stops being tidy. Each of these is a machine doing something the happy path never
/// expects - bytes that are not frames, two people who chose the same name, a laptop walked out of range
/// while it was carrying a file, a room deleted from under somebody, a send on a connection that has already
/// gone. None of them may end the room, and none of them may leave a window waiting for something that will
/// never arrive.
/// </summary>
public sealed class EdgeCaseTests : IAsyncLifetime
{
    private readonly List<Guid> transfers = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // A half file is the one thing a test may not leave anywhere: the spool lives in the user's own app
        // data, not in a temp folder of the test's choosing.
        foreach (var id in transfers)
        {
            foreach (var leftover in SpoolFiles(id))
            {
                TryDelete(leftover);
            }
        }

        await Task.CompletedTask;
    }

    // ---------------------------------------------------------- two of a name

    [Fact]
    public async Task TwoPeopleOfANameAreBothLetInAndTheRoomIsTold()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var cara = new ChatClient("Cara");
        await using var alsoCara = new ChatClient("cara");
        await cara.ConnectAsync(server.Endpoint);
        await alsoCara.ConnectAsync(server.Endpoint);

        await cara.JoinByNameWhenShownAsync("Room 1");
        await Watch.UntilAsync(() => MembersOf(server, "Room 1") == 2, "the first Cara to be in the room");

        var atTheHost = WatchFrames(host);
        var heardByTheFirst = WatchMessages(cara);

        // Not the same keystroke and no malice: two people on two laptops who both thought of their own cat.
        // They are both let in - a nickname is chosen once on the way into the program and cannot be changed
        // from inside a room, so refusing the second one would leave them outside with nothing they could do.
        await alsoCara.JoinByNameWhenShownAsync("Room 1");

        await Watch.UntilAsync(() => MembersOf(server, "Room 1") == 3, "the second Cara to be in the room");

        Assert.Equal("Room 1", alsoCara.CurrentRoomName);

        // Only the first letter differs, which is the case a case-sensitive comparison would have missed:
        // "cara" and "Cara" read identically to anybody looking at the list.
        var notice = await atTheHost.ItemAsync(
            f => f.Type == FrameType.SYSTEM_NOTICE
                && (f.Header.Text ?? string.Empty).Contains("called cara", StringComparison.OrdinalIgnoreCase),
            "the room to hear that there are two of the name");

        // Named by the name as it was given, which for the second of them is "cara": the notice is about the
        // person who just walked in, and the count is what tells the room how many there now are.
        Assert.Contains("there are 2 people in this room called cara", notice.Header.Text, StringComparison.OrdinalIgnoreCase);

        // The room is told, not the person turned away - so the notice arrives as a line of the transcript in
        // every window in there, including the one belonging to the first Cara, who is the person most in
        // need of knowing that the next "Cara" on screen may not be the one they were answering.
        var inTheTranscript = await heardByTheFirst.ItemAsync(
            m => m is SystemMessage s && s.Body.Contains("called cara", StringComparison.OrdinalIgnoreCase),
            "the first Cara's window to show it");

        Assert.Equal("Room 1", inTheTranscript.RoomName);
    }

    [Fact]
    public async Task WearingTheHostsNameIsAllowedButTheRoomHearsAboutItToo()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var clone = new ChatClient("Hosty");
        await clone.ConnectAsync(server.Endpoint);

        var atTheHost = WatchFrames(host);

        await clone.JoinByNameWhenShownAsync("Room 1");

        // The host's window is a member like any other, so a client arriving wearing its name is the sharpest
        // case there is: two lines attributed to the host, one of them not from the host. Even so the answer
        // is for the room to be told rather than for the stranger to be turned away and left stranded - a
        // host who does not want them has ways of dealing with that once they are visible.
        var notice = await atTheHost.ItemAsync(
            f => f.Type == FrameType.SYSTEM_NOTICE
                && (f.Header.Text ?? string.Empty).Contains("called Hosty", StringComparison.Ordinal),
            "the room to hear there are two Hostys");

        Assert.Contains("2", notice.Header.Text, StringComparison.Ordinal);
        Assert.Equal("Room 1", clone.CurrentRoomName);
        Assert.Equal(2, MembersOf(server, "Room 1"));
    }

    [Fact]
    public async Task TheSameNameInTwoDifferentRoomsIsNobodysBusiness()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        // A name of the test's own, because a host opens with three rooms already in it.
        Assert.True(server.TryCreateRoom("Study", out var created), created ?? "the room was created");

        await using var one = new ChatClient("Cara");
        await using var other = new ChatClient("Cara");
        await one.ConnectAsync(server.Endpoint);
        await other.ConnectAsync(server.Endpoint);

        await one.JoinByNameWhenShownAsync("Room 1");
        await Watch.UntilAsync(() => MembersOf(server, "Room 1") == 2, "the first Cara to be in Room 1");

        // Watched at the host's window rather than at Cara's: a room does not echo a person's own words back
        // to them, so the host is the witness in Room 1 that can be relied on.
        var atTheHost = WatchFrames(host);

        // A name is only ever used inside one room, so that is the width of the rule too: two Caras in two
        // rooms are not two people one room cannot tell apart, and no room is entitled to know who is called
        // what in another one.
        await other.JoinByNameWhenShownAsync("Study");

        // One member rather than two: the host's window stayed in Room 1, and nobody else is in Study.
        await Watch.UntilAsync(() => MembersOf(server, "Study") == 1, "the second Cara to be in Study");

        // Room 1 hears nothing about the other Cara. A word is waited for first, because a missing notice is
        // only worth asserting once the room has had its turn to say something.
        await one.SendAsync(new TextMessage("Cara", "Room 1", "still only one of me here"));
        await atTheHost.ItemAsync(
            f => f.Type == FrameType.TEXT && f.Header.Text == "still only one of me here",
            "Room 1 to carry on as itself");

        Assert.DoesNotContain(
            atTheHost.Items,
            f => f.Type == FrameType.SYSTEM_NOTICE && (f.Header.Text ?? string.Empty).Contains("called Cara", StringComparison.Ordinal));

        Assert.Equal("Room 1", one.CurrentRoomName);
        Assert.Equal("Study", other.CurrentRoomName);
    }

    // ------------------------------------------------------------ broken bytes

    [Fact]
    public async Task GarbageOnTheWireEndsOneConnectionAndLeavesTheRoomAlone()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");

        // Where the next message is looked for is the host's own window, not Dov's: a room does not echo a
        // person's own words back at them, so watching the sender would be watching nothing.
        var atTheHost = new Watch<Frame>();
        host.FrameReceived += (_, frame) => atTheHost.Add(frame);

        // Somebody's phone, a second program on the same laptop, or a machine that means harm: bytes that
        // are not Lantern frames at all. The room is not allowed to be anybody's collateral damage - the one
        // connection that spoke nonsense is the one that ends.
        using (var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint))
        {
            await raw.Output.WriteAsync(new byte[] { (byte)'n', (byte)'o', 0, 1, 2, 255, (byte)'x' });
            await raw.Output.FlushAsync();

            using var read = new CancellationTokenSource(Watch.DefaultTimeoutMs);

            var ended = false;

            try
            {
                // Read on until this connection is finished with, one way or another: the host may say
                // something back first - a refusal is a fair answer to nonsense - and either a clean end or a
                // stream that broke means the same thing here. What must not happen is the connection going
                // on as though nothing had been said.
                while (true)
                {
                    if (await FrameCodec.ReadAsync(raw.Input, read.Token) is null)
                    {
                        ended = true;
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is FrameException or IOException or SocketException or ObjectDisposedException)
            {
                ended = true;
            }

            Assert.True(ended, "the connection that spoke nonsense should have been the one closed");
        }

        await Watch.UntilAsync(() => server.Rooms.Any(r => r.Name == "Room 1"), "Room 1 to be open still");

        // And nobody else felt it: the room carries on talking, and the host is still the host.
        await dov.SendAsync(new TextMessage("Dov", "Room 1", "still here"));
        Assert.NotNull(await atTheHost.ItemAsync(
            f => f.Type == FrameType.TEXT && f.Header.Text == "still here",
            "the room to carry on"));

        Assert.True(server.IsRunning);
        Assert.NotNull(server.RoomOf(host));
        Assert.True(server.TryCreateRoom("Room B", out var created), created ?? "the host should still be able to edit rooms");
    }

    // --------------------------------------------------------- lost half files

    [Fact]
    public async Task AFileThatLostItsSenderHalfwayIsOverAndLeavesNoSpoolBehind()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offered = WatchJobs(dov);

        // A laptop walking out of range with a file half sent: the next chunk simply never arrives, and
        // there is no FILE_DONE to wait for because the machine that owed it has gone. Sitting on the flow
        // timeout instead would leave a bar pointing at a number it will never reach for twenty seconds.
        var id = Guid.NewGuid();
        transfers.Add(id);

        var payload = Bytes(4096);

        // What happened to this transfer is what the test is about, and the asking happens after the sender
        // has gone, so it is declared outside the connection that carried it.
        FileTransferJob job;

        using (var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint))
        {
            await SendRawAsync(raw, Frame.Hello("Zed"), Frame.JoinRoom(server.Id("Room 1")));
            await SendRawAsync(
                raw,
                Frame.FileMeta(Guid.NewGuid(), id, server.Id("Room 1"), "Room 1", "Zed", "holiday.zip", payload.LongLength),
                Frame.FileChunk(id, 0, payload[..2048]));

            // Offered first, and only then gone: closing the link before the host had even read the
            // announcement would test the socket's goodbye rather than the room's answer.
            job = await offered.ItemAsync(j => j.FileName == "holiday.zip", "Dov to be offered the file");

            raw.Close();
        }

        // Half way there, and the machine that owed the rest of it is no longer in range.
        await Watch.UntilAsync(() => job.Finished.IsCompleted, "the half file to be given up");

        Assert.False(job.IsComplete);
        Assert.True(job.Cancel.IsCancellationRequested, "a file with no sender left is a file that has stopped");
        Assert.Empty(SpoolFiles(id));
    }

    [Fact]
    public async Task AFileArrivingWhenTheConnectionGoesIsGivenUpRatherThanCountedForever()
    {
        var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offered = WatchJobs(dov);
        var lost = new Watch<Exception>();
        dov.ConnectionLost += (_, ex) => lost.Add(ex);

        var id = Guid.NewGuid();
        transfers.Add(id);

        var payload = Bytes(4096);
        FileTransferJob job;

        using (var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint))
        {
            await SendRawAsync(raw, Frame.Hello("Zed"), Frame.JoinRoom(server.Id("Room 1")));
            await SendRawAsync(
                raw,
                Frame.FileMeta(Guid.NewGuid(), id, server.Id("Room 1"), "Room 1", "Zed", "photo.zip", payload.LongLength),
                Frame.FileChunk(id, 0, payload[..1024]));

            await offered.ItemAsync(j => j.FileName == "photo.zip", "Dov to be offered the file");

            // The host quits, or this laptop's own radio dies: the connection goes out from under a file this
            // machine was in the middle of receiving.
            await server.StopAsync();
        }

        await lost.ItemAsync(_ => true, "Dov to notice the connection is gone");

        // Before this was handled, nothing failed here at all: the job simply went on being unfinished, which
        // to a window means a progress bar that has stopped moving for ever, and a .part in the incoming
        // folder that nobody will think to open next month.
        job = await offered.ItemAsync(j => j.FileName == "photo.zip", "the file that was arriving");
        await Watch.UntilAsync(() => job.Finished.IsCompleted, "the half file to be given up");

        Assert.True(job.Cancel.IsCancellationRequested, "a file whose connection is gone is a file that has stopped");
        Assert.False(job.IsComplete);
        Assert.Empty(SpoolFiles(id));
    }

    // ---------------------------------------------------- sending into thin air

    [Fact]
    public async Task SendingAFileOverAGoneConnectionEndsTheJobRatherThanHanging()
    {
        var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");

        var file = TempFile("notes.txt", Bytes(20_000));

        await server.StopAsync();
        await Watch.UntilAsync(() => !dov.IsConnected, "Dov to notice the host has gone");

        // The person has already chosen the file and the connection went a moment after, which is a normal
        // Tuesday. That has to come back as an ended transfer with a line about it - not as a bar that never
        // moves again, and not as an exception on whichever thread happened to be reading the disk.
        FileTransferJob job;

        try
        {
            job = dov.SendFile(file);
        }
        catch (InvalidOperationException refused)
        {
            // Also an answer, and a friendlier one: refused in words while they are still standing there.
            Assert.Contains("room", refused.Message, StringComparison.OrdinalIgnoreCase);
            return;
        }

        using var wait = new CancellationTokenSource(Watch.DefaultTimeoutMs);

        try
        {
            await job.Finished.WaitAsync(wait.Token);
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException("the transfer never ended: it is still waiting on a connection that is gone");
        }

        Assert.False(job.IsComplete);
        Assert.Equal(0, job.BytesDone);
    }

    [Fact]
    public async Task TalkingAfterTheConnectionGoesIsRefusedInWords()
    {
        var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");

        await server.StopAsync();
        await Watch.UntilAsync(() => !dov.IsConnected, "Dov to notice");

        // The Send button may still be lit for a moment after the radio dies, and a person typing away has no
        // way of knowing. What they must get back is a sentence, promptly, and not a hang.
        var problem = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dov.SendAsync(new TextMessage("Dov", "Room 1", "anybody there?")));

        Assert.Contains("connect", problem.Message, StringComparison.OrdinalIgnoreCase);

        // A picture is a frame like any other, so it goes the same way rather than ending the window.
        var pictureProblem = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dov.SendAsync(new PictureMessage("Dov", "Room 1", "holiday.png", [1, 2, 3])));

        Assert.Contains("connect", pictureProblem.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ in the window

    [Fact]
    public async Task ARoomClosedUnderSomeoneWhoseMessageIsHalfTyped()
    {
        await Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new PlainForm(service);

            form.Show();
            ((ListBox)Find(form, "lstRooms")).SelectedIndex = 0;
            await Watch.UntilAsync(() => form.RoomNameNow is not null, "the room to be joined");

            var typed = "this is what I was writing when the host deleted the room";
            ((TextBox)Find(form, "txtMessage")).Text = typed;

            // The host deletes the room this person is typing in - scenario 3 done unkindly. Nothing may be
            // thrown at them, and nothing they typed may vanish: the room went, the words stayed.
            service.SimulateRoomClosed("Room 1");
            await Watch.UntilAsync(() => form.RoomNameNow is null, "the window to notice the room is gone");

            Assert.Equal(typed, ((TextBox)Find(form, "txtMessage")).Text);

            // With no room there is nowhere for the words to go, so the way out is greyed rather than waiting
            // to fail. Pressing it anyway - which is what a person who is not looking does - changes nothing.
            var send = (Button)Find(form, "btnSend");
            Assert.False(send.Enabled);

            send.PerformClick();
            Application.DoEvents();

            Assert.Equal(typed, ((TextBox)Find(form, "txtMessage")).Text);
            Assert.DoesNotContain(service.Transcript, m => m is TextMessage t && t.Body == typed);

            // And the window is still a window: it can be pointed at another room when one appears.
            Assert.False(form.IsDisposed);

            form.Dispose();
        });
    }

    [Fact]
    public async Task HostingWithoutARadioIsToldInWordsAndTheWindowIsStillThere()
    {
        await Sta.RunAsync(async () =>
        {
            // The demo laptop with the radio switched off, which is the way F2 fails in a lab: nothing on
            // screen says "Bluetooth", the button is pressed, and a person is left with a window that has not
            // changed. The name of the thing to check has to be in the sentence.
            using var form = new Lantern.UI.StartupForm(new NoRadioAtAll());

            form.Show();
            ((TextBox)Find(form, "txtNickname")).Text = "Haitao";
            ((Button)Find(form, "btnStartServer")).PerformClick();

            await Watch.UntilAsync(
                () => form.StatusText.Contains("could not host", StringComparison.OrdinalIgnoreCase),
                "the window to say it could not host");

            Assert.Contains("radio", form.StatusText, StringComparison.OrdinalIgnoreCase);

            // No dialog to dismiss, no crashed window, and the two doors still open: the same person who
            // switches the radio on, or runs on loopback instead, tries again from where they are.
            Assert.False(form.IsDisposed);
            Assert.True(((Button)Find(form, "btnStartServer")).Enabled, "hosting should be possible to attempt again");
            Assert.True(((Button)Find(form, "btnScan")).Enabled, "scanning should still be offered");

            form.Dispose();
        });
    }

    // ------------------------------------------------------------------ the furniture

    /// <summary>A machine that says no when asked for a Bluetooth server, which is what a switched-off radio does.</summary>
    private sealed class NoRadioAtAll : ITransportFactory
    {
        public string Name => "Bluetooth";

        public string Description => "Bluetooth between two laptops";

        public IBtServer NewServer(string serverName) => new NoRadioServer();

        public IBtClient NewClient() => new BtLoopbackClient();
    }

    private sealed class NoRadioServer : IBtServer
    {
        public Task StartAsync(Guid serviceId)
            => throw new IOException(
                "The Bluetooth radio refused to listen. Another Lantern on this machine may still be hosting, or the radio is off.");

        public Task<IClientLink?> AcceptAsync(CancellationToken cancel = default) => Task.FromResult<IClientLink?>(null);

        public void Stop()
        {
            // Never got as far as listening, so there is nothing to stop.
        }

        public bool IsListening => false;

        public ServerEndpoint Endpoint => throw new InvalidOperationException("A server that is not listening has no address to advertise.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>A chat window with nothing overridden, for the cases where the point is what the window does by default.</summary>
    private sealed class PlainForm : Lantern.UI.ChatForm
    {
        public PlainForm(IChatService service) : base(service)
        {
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string? RoomNameNow => CurrentRoom?.Name;
    }

    private static Control Find(Control parent, string name)
        => parent.Controls.Find(name, searchAllChildren: true).FirstOrDefault()
            ?? throw new InvalidOperationException($"no control named {name}");

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

        await Watch.UntilAsync(() => clients.All(c => c.CurrentRoomName == roomName), $"the joins to reach {roomName}");
    }

    private static int MembersOf(ChatServer server, string roomName)
        => server.Rooms.FirstOrDefault(r => r.Name == roomName)?.Members.Count ?? 0;

    /// <summary>The spool files one transfer has left in the user's incoming folder, found by transfer id.</summary>
    private static IReadOnlyList<string> SpoolFiles(Guid transferId)
    {
        var directory = ReceivedPayloads.IncomingDirectory;

        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, $"{transferId:N}-*.part");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A spool still open on a connection that has not noticed it is gone will be deleted by whoever
            // owns it; a test is not the place to fight a file lock.
        }
    }

    private static async Task SendRawAsync(IClientLink link, params Frame[] frames)
    {
        foreach (var frame in frames)
        {
            await FrameCodec.WriteAsync(link.Output, frame);
        }

        await link.Output.FlushAsync();
    }

    private static Watch<Frame> WatchFrames(LocalParticipant participant)
    {
        var watch = new Watch<Frame>();
        participant.FrameReceived += (_, frame) => watch.Add(frame);
        return watch;
    }

    private static Watch<FileTransferJob> WatchJobs(IChatService service)
    {
        var watch = new Watch<FileTransferJob>();
        service.IncomingFile += (_, job) => watch.Add(job);
        return watch;
    }

    private static Watch<Message> WatchMessages(ChatClient client)
    {
        var watch = new Watch<Message>();
        client.MessageReceived += (_, message) => watch.Add(message);
        return watch;
    }

    private static byte[] Bytes(int count)
    {
        var bytes = new byte[count];

        // A pattern rather than zeroes, so that a chunk written twice or skipped is a mismatch instead of a
        // plausible run of nothing.
        for (var i = 0; i < count; i++)
        {
            bytes[i] = (byte)(i * 31 + (i >> 8) * 7);
        }

        return bytes;
    }

    private string TempFile(string name, byte[] contents)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lantern-edge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, contents);
        return path;
    }
}
