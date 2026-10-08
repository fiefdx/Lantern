using System.Diagnostics;
using Lantern.Core;
using Lantern.Domain;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T2.7: the host's own window talking to the real <see cref="ChatServer"/> through
/// <see cref="HostChatService"/>. The point of these tests is that the host is not special: it joins,
/// speaks, edits rooms and leaves through the same rules a client does, because it is a participant
/// like any other.
/// </summary>
public sealed class HostChatServiceTests
{
    [Fact]
    public async Task TheHostWindowKnowsItsOwnRoomsBeforeAnyoneConnects()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            Assert.True(service.IsHost);
            Assert.Equal("Hosty", service.Nickname);
            Assert.Equal(["Room 1", "Room 2", "Room 3"], service.Rooms.Select(r => r.Name));
            Assert.Empty(service.Members);
        }
    }

    [Fact]
    public async Task TheHostWindowSpeaksAndTheClientHearsIt()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            await service.JoinRoomAsync("Room 1");

            await using var client = new ChatClient("Cara");
            var received = WatchMessages(client);
            await client.ConnectAsync(server.Endpoint);
            await client.JoinRoomAsync("Room 1");
            await Watch.UntilAsync(() => client.Members.Count == 2, "both members in the list");

            await service.SendAsync(new TextMessage(service.Nickname, "Room 1", "welcome, make yourself at home"));

            var line = await received.ItemAsync(m => m is TextMessage, "the host's line");
            Assert.Equal("welcome, make yourself at home", ((TextMessage)line).Body);
            Assert.Equal("Hosty", line.SenderNickname);
        }
    }

    [Fact]
    public async Task TheClientSpeaksAndTheHostWindowHearsIt()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            await service.JoinRoomAsync("Room 2");
            var received = WatchMessages(service);

            await using var client = new ChatClient("Cara");
            await client.ConnectAsync(server.Endpoint);
            await client.JoinRoomAsync("Room 2");
            await Watch.UntilAsync(() => service.Members.Count == 2, "the host's list to grow");

            await client.SendAsync(new TextMessage("Cara", "Room 2", "can the host see me?"));

            var line = await received.ItemAsync(m => m is TextMessage, "the client's line");
            Assert.Equal("Cara", line.SenderNickname);
            Assert.Equal("Room 2", line.RoomName);
        }
    }

    [Fact]
    public async Task TheHostKeepsItsOwnLinesInTheTranscriptEvenThoughNothingEchoesBack()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            await service.JoinRoomAsync("Room 1");
            await service.SendAsync(new TextMessage(service.Nickname, "Room 1", "a line I said myself"));

            var room = service.Rooms.Single(r => r.Name == "Room 1");
            await Watch.UntilAsync(
                () => room.Transcript.Any(m => m.Preview.Contains("a line I said myself")),
                "the host's own line to be part of the room's history");
        }
    }

    [Fact]
    public async Task BothSidesSeeTheSameMemberListIncludingThemselves()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            await service.JoinRoomAsync("Room 3");

            await using var one = new ChatClient("Cara");
            await using var two = new ChatClient("Davide");
            await one.ConnectAsync(server.Endpoint);
            await two.ConnectAsync(server.Endpoint);
            await one.JoinRoomAsync("Room 3");
            await two.JoinRoomAsync("Room 3");

            await Watch.UntilAsync(() => one.Members.Count == 3 && two.Members.Count == 3 && service.Members.Count == 3, "three members");

            var expected = new[] { "Cara", "Davide", "Hosty" };
            Assert.Equal(expected, service.Members.Select(u => u.Nickname).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(expected, one.Members.Select(u => u.Nickname).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(expected, two.Members.Select(u => u.Nickname).OrderBy(n => n, StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task ANoticeAboutJoiningReachesEverybodyInTheRoom()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            await service.JoinRoomAsync("Room 1");
            var hostMessages = WatchMessages(service);

            await using var client = new ChatClient("Cara");
            var clientMessages = WatchMessages(client);
            await client.ConnectAsync(server.Endpoint);
            await client.JoinRoomAsync("Room 1");

            await Watch.UntilAsync(
                () => hostMessages.Items.Any(m => m.Preview.Contains("Cara - join")),
                "the host to hear about the arrival");
            await Watch.UntilAsync(
                () => clientMessages.Items.Any(m => m.Preview.Contains("Cara - join")),
                "the newcomer to see the room's notice about itself");
        }
    }

    [Fact]
    public async Task RoomsEditedThroughTheServiceReachTheClientsLive()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            await using var client = new ChatClient("Cara");
            await client.ConnectAsync(server.Endpoint);

            await service.CreateRoomAsync("Quiet Corner");
            await Watch.UntilAsync(() => client.Rooms.Any(r => r.Name == "Quiet Corner"), "the new room to arrive");

            await service.RenameRoomAsync("Quiet Corner", "Even Quieter");
            await Watch.UntilAsync(() => client.Rooms.Any(r => r.Name == "Even Quieter"), "the rename to arrive");

            await service.DeleteRoomAsync("Room 3");
            await Watch.UntilAsync(() => !client.Rooms.Any(r => r.Name == "Room 3"), "the deletion to arrive");

            Assert.Equal(["Room 1", "Room 2", "Even Quieter"], service.Rooms.Select(r => r.Name));
        }
    }

    [Fact]
    public async Task ARefusedRoomEditSaysWhyToTheWindow()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            var problem = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRoomAsync("Room 1"));
            Assert.Contains("already exists", problem.Message);

            // ChatForm turns that into the status bar, so the message has to read as advice, not as a
            // stack trace.
            Assert.DoesNotContain("Exception", problem.Message);
        }
    }

    [Fact]
    public async Task DeletingTheRoomTheHostIsStandingInPutsTheHostNowhere()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            await service.JoinRoomAsync("Room 2");
            Assert.Contains("Room 2", service.Rooms.Select(r => r.Name));

            await service.DeleteRoomAsync("Room 2");

            await Watch.UntilAsync(
                () => !service.Rooms.Any(r => r.Name == "Room 2") && service.Members.Count == 0,
                "the host's window to catch up with its own deletion");
        }
    }

    [Fact]
    public async Task TheHostWindowDoesNotConnectToAChat()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConnectAsync(server.Endpoint));
        }
    }

    [Fact]
    public async Task ClosingTheHostEndsTheChatForEverybody()
    {
        var (server, service) = await StartAsync();
        await using (server)
        {
            await service.JoinRoomAsync("Room 1");

            await using var client = new ChatClient("Cara");
            var lost = new Watch<Exception>();
            await client.ConnectAsync(server.Endpoint);
            await client.JoinRoomAsync("Room 1");
            client.ConnectionLost += (_, reason) => lost.Add(reason);

            // This is what ChatForm does when the host closes the window (T2.7).
            await service.ShutdownAsync();

            var reason = await lost.ItemAsync(_ => true, "the client to notice");
            Assert.False(string.IsNullOrWhiteSpace(reason.Message));
        }
    }

    [Fact]
    public async Task AnOversizeMessageIsRefusedBeforeAnythingMoves()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            await service.JoinRoomAsync("Room 1");

            await using var client = new ChatClient("Cara");
            var received = WatchMessages(client);
            await client.ConnectAsync(server.Endpoint);
            await client.JoinRoomAsync("Room 1");
            await Watch.UntilAsync(() => client.Members.Count == 2, "the join");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SendAsync(new TextMessage("Hosty", "Room 1", new string('x', ValidationRules.MessageMaxLength + 1))));

            await Task.Delay(150);
            Assert.Equal(0, received.CountMatch(m => m is TextMessage));
        }
    }

    /// <summary>
    /// T5.3: the host window sends a file the way every other window does - it is handed a job to watch,
    /// and the call comes back at once, because a window that could not be typed in while it sent an
    /// attachment would be a window nobody used (F8).
    /// </summary>
    [Fact]
    public async Task TheHostWindowSendsAFileWithoutHoldingTheWindowStill()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            await service.JoinRoomAsync("Room 1");

            // In a folder of its own so the file keeps the name the room is told about.
            var folder = Path.Combine(Path.GetTempPath(), "lantern-host-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, "notes.bin");
            await File.WriteAllBytesAsync(path, new byte[20_000]);

            try
            {
                var started = Stopwatch.StartNew();

                var job = service.SendFile(path);
                var returnedAfter = started.Elapsed;

                Assert.Equal("notes.bin", job.FileName);
                Assert.Equal(20_000, job.SizeBytes);
                Assert.Equal("Hosty", job.SenderNickname);

                await job.Finished;

                // The call that started it did not wait for the file: twenty thousand bytes is small, so
                // the point is the shape rather than the number - a five second wait here would be a
                // frozen window for a real attachment.
                Assert.True(returnedAfter < TimeSpan.FromSeconds(1), $"SendFile waited {returnedAfter} for the file to go");

                // The job is the progress: bytes counted as they leave, and the fraction is what a bar in
                // this window would show. Progress reports themselves are tested where the reporting happens
                // (see DomainModelTests), because subscribing after the call returns is a race a test has
                // no business winning.
                Assert.True(job.IsComplete);
                Assert.Equal(1.0, job.Fraction);

                // A file is not a message, so building one and sending it is a mistake about the API.
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.SendAsync(new FileMessage("Hosty", "Room 1", "notes.pdf", 12)));
            }
            finally
            {
                File.Delete(path);

                try
                {
                    Directory.Delete(folder);
                }
                catch (IOException)
                {
                    // Somebody else's test is still using the scratch directory, which is its business.
                }
            }
        }
    }

    // ------------------------------------------------------ a window that throws

    [Fact]
    public async Task AThrowingHostWindowLeavesTheServerIntact()
    {
        var (server, service) = await StartAsync();
        await using (server)
        await using (service)
        {
            await service.JoinRoomAsync("Room 1");

            await using var client = new ChatClient("Cara");
            await client.ConnectAsync(server.Endpoint);
            await client.JoinRoomAsync("Room 1");
            await Watch.UntilAsync(() => client.Members.Count == 2, "both members in the list");

            // The host's window is reached synchronously, from the server's own threads: if its exception
            // were allowed back in, it would unwind a broadcast or a join halfway through, and the room
            // would be left holding whoever had been dealt with so far.
            service.MessageReceived += (_, _) => throw new InvalidOperationException("the window is closed");

            await client.SendAsync(new TextMessage("Cara", "Room 1", "first"));
            await client.SendAsync(new TextMessage("Cara", "Room 1", "second"));

            // The frames were still applied, because the state is changed before anybody is notified.
            await Watch.UntilAsync(
                () => service.Rooms.Single(r => r.Name == "Room 1").Transcript.Count(m => m is TextMessage) == 2,
                "the host's transcript to hold both lines");

            // And the chat carries on: a third line reaches the client's own window from the host.
            var fromClient = WatchMessages(client);
            await service.SendAsync(new TextMessage(service.Nickname, "Room 1", "third"));
            await fromClient.ItemAsync(m => m.Preview == "third", "the client to hear the host afterwards");
            Assert.True(client.IsConnected);
        }
    }

    // ---------------------------------------------------------------- plumbing

    /// <summary>The same sequence <see cref="Lantern.UI.StartupForm"/> runs when "Host" is clicked.</summary>
    private static async Task<(ChatServer Server, HostChatService Service)> StartAsync()
    {
        var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host");
        await server.StartAsync();

        var service = new HostChatService(server, "Hosty");
        await service.StartAsync();

        return (server, service);
    }

    private static Watch<Message> WatchMessages(IChatService service)
    {
        var watch = new Watch<Message>();
        service.MessageReceived += (_, message) => watch.Add(message);
        return watch;
    }
}
