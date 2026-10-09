using Lantern.Core;
using Lantern.Domain;
using Lantern.Storage;
using Lantern.Transport;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T3.2 and T3.3 from the engine's side: the room list is the host's, it is written whenever it
/// changes, and a file that will not be written is a status line rather than a broken chat (F9).
/// </summary>
public sealed class RoomPersistenceTests
{
    [Fact]
    public async Task TheRoomsComeFromTheFileRatherThanTheDefaults()
    {
        using var file = new TempRoomFile("""{"version":1,"rooms":[{"name":"Kitchen Table"},{"name":"Garden"}]}""");

        using var store = new JsonRoomStore(file.Path);
        await using var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host", roomStore: store);

        // The three names everybody expects are the ones a first run gets, not a right answer: the host
        // renamed and deleted rooms last week and that is what should be on screen tonight.
        Assert.Equal(["Kitchen Table", "Garden"], server.Rooms.Select(r => r.Name));
    }

    [Fact]
    public async Task TheRoomsGivenInCodeAreUsedWhenThereIsNoStore()
    {
        // A test or a demo with no file to keep: the rooms live and die with the process.
        await using var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host", ["Only Room"]);

        Assert.Equal(["Only Room"], server.Rooms.Select(r => r.Name));
    }

    [Fact]
    public async Task EveryChangeToTheRoomsIsWrittenToTheFile()
    {
        using var file = new TempRoomFile();
        using var store = new JsonRoomStore(file.Path);
        await using var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host", roomStore: store);

        Assert.True(server.TryCreateRoom("Garden", out _));
        Assert.True(server.TryRenameRoom(server.Id("Room 1"), "Kitchen Table", out _));
        Assert.True(server.TryDeleteRoom(server.Id("Room 3"), out _));
        await server.SavesCompletedAsync();

        // Read it back with a store of its own, which is what the next start of the program does.
        using var reopened = new JsonRoomStore(file.Path);
        Assert.Equal(["Kitchen Table", "Room 2", "Garden"], reopened.Load().Select(r => r.Name));
    }

    [Fact]
    public async Task ARoomDeletedByTheHostStaysDeletedAfterARestart()
    {
        using var file = new TempRoomFile();

        using (var store = new JsonRoomStore(file.Path))
        {
            await using var first = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host", roomStore: store);
            Assert.True(first.TryDeleteRoom(first.Id("Room 2"), out _));
            await first.SavesCompletedAsync();
        }

        using var reopened = new JsonRoomStore(file.Path);
        Assert.Equal(["Room 1", "Room 3"], reopened.Load().Select(r => r.Name));
    }

    [Fact]
    public async Task AChangeMadeImmediatelyBeforeQuittingIsStillThereAfterwards()
    {
        using var file = new TempRoomFile();
        using var store = new JsonRoomStore(file.Path);
        var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host", roomStore: store);

        await server.StartAsync();
        Assert.True(server.TryCreateRoom("Added just before quit", out _));

        // File → Quit, straight after editing the rooms. Nothing is queued away out of existence: the
        // server waits for what it has promised to write, for a little while anyway.
        await server.StopAsync();

        using var reopened = new JsonRoomStore(file.Path);
        Assert.Contains("Added just before quit", reopened.Load().Select(r => r.Name));
    }

    [Fact]
    public async Task AStoreThatRefusesToWriteDoesNotStopTheChat()
    {
        await using var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host", roomStore: new GrumpyStore());

        var status = new List<string>();
        server.StatusChanged += (_, text) => status.Add(text);

        Assert.True(server.TryCreateRoom("Garden", out _));
        await server.SavesCompletedAsync();

        // The room exists, is listed, and can be joined. Only the future restart is affected, and the
        // host is told in the words the store used.
        Assert.Contains("Garden", server.Rooms.Select(r => r.Name));
        Assert.Contains(status, s => s.Contains("the disk is full", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStoreThatThrowsIsReportedAndNotRethrown()
    {
        await using var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host", roomStore: new ExplodingStore());

        var status = new List<string>();
        server.StatusChanged += (_, text) => status.Add(text);

        // The save happens on its own task, so an exception there would otherwise vanish - or, before
        // the guard, fault a task nobody awaits. Either way the chat must not care.
        Assert.True(server.TryCreateRoom("Garden", out _));
        await server.SavesCompletedAsync();

        Assert.Contains("Garden", server.Rooms.Select(r => r.Name));
        Assert.Contains(status, s => s.Contains("room list", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AClientConnectsToTheRoomsInTheFileNotTheOnesTheCodeNamed()
    {
        using var file = new TempRoomFile("""{"version":1,"rooms":[{"name":"Garden"}]}""");
        using var store = new JsonRoomStore(file.Path);
        await using var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host", ["Ignored"], store);
        await server.StartAsync();

        await using var client = new ChatClient("Cara");
        await client.ConnectAsync(server.Endpoint);
        await Watch.UntilAsync(() => client.Rooms.Any(r => r.Name == "Garden"), "the client to be told the rooms");

        Assert.DoesNotContain("Ignored", client.Rooms.Select(r => r.Name));
    }

    // ----------------------------------------------- the window is the quit button (F9)

    [Fact]
    public async Task AWritePromisedOnTheWayOutIsFinishedBeforeTheProcessGoesAway()
    {
        using var file = new TempRoomFile();
        using var store = new SlowStore(file.Path);
        await using var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host", roomStore: store);
        await server.StartAsync();
        await using var host = new HostChatService(server, "Haitao");
        await host.StartAsync();

        Assert.True(server.TryCreateRoom("Written while closing", out _));

        // The ✕ on the title bar, Alt+F4 and File ▸ Quit all end in the same place, and this write takes a
        // moment. The window does not stand still for it any more - it starts the stopping and hands the
        // task over - so the promise has to be kept by whoever holds that task, which is what the second
        // half of this test is.
        Task? stopping = null;
        var stillRunning = false;

        Sta.Run(() =>
        {
            // A plain HostForm would ask whether the user is sure, and a question nobody is there to
            // answer is a test that never ends - so the answer is supplied.
            using var form = new AnsweringHost(host) { Confirms = true };
            form.Show();
            form.Close();

            stopping = form.StoppingChat;
            stillRunning = !stopping.IsCompleted;
        });

        Assert.True(stillRunning, "the window waited on a write that had five more seconds to go");

        // The start screen waits on this before it lets another chat start, and again before the program
        // exits, which is how the room still lands in the file. Skipping this line is the half of F9 that
        // only restarting reveals: the room comes back.
        await stopping!;

        Assert.False(server.IsRunning);
        Assert.True(File.Exists(file.Path), "the room list had not been written by the time the chat had stopped");
        Assert.Contains("Written while closing", File.ReadAllText(file.Path));
    }

    [Fact]
    public async Task ClosingTheHostWindowIsAskedForWhicheverWayItWasAsked()
    {
        await using var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host");
        await server.StartAsync();
        await using var host = new HostChatService(server, "Haitao");
        await host.StartAsync();

        Sta.Run(() =>
        {
            using var form = new AnsweringHost(host) { Confirms = false };
            form.Show();
            form.Close();

            // "Are you sure" answered no has to mean the window is still there and everybody connected
            // stays connected - the ✕ is not an escape hatch out of that question.
            Assert.True(form.Visible);
            Assert.True(server.IsRunning);

            form.Confirms = true;
            form.Close();

            Assert.False(form.Visible);
            Assert.False(server.IsRunning);
        });
    }

    /// <summary>A host window with a person answering the "are you sure" question.</summary>
    private sealed class AnsweringHost : HostForm
    {
        public AnsweringHost(IChatService service)
            : base(service)
        {
        }

        public bool Confirms = true;

        protected override bool ConfirmQuit() => Confirms;
    }

    /// <summary>
    /// A store that writes, slowly - so that a shutdown which does not wait can be caught not waiting.
    /// </summary>
    private sealed class SlowStore : IRoomStore, IDisposable
    {
        private readonly JsonRoomStore inner;

        public SlowStore(string path) => inner = new JsonRoomStore(path);

        public event EventHandler<string>? SaveFailed
        {
            add { }
            remove { }
        }

        public List<Room> Load() => inner.Load();

        public async Task SaveAsync(IEnumerable<Room> rooms, CancellationToken cancel = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cancel).ConfigureAwait(false);
            await inner.SaveAsync(rooms, cancel).ConfigureAwait(false);
        }

        public void Dispose() => inner.Dispose();
    }

    /// <summary>A store that cannot write and says so, which is what <see cref="IRoomStore"/> asks for.</summary>
    private sealed class GrumpyStore : IRoomStore
    {
        public event EventHandler<string>? SaveFailed;

        public List<Room> Load() => [new Room("Room 1")];

        public Task SaveAsync(IEnumerable<Room> rooms, CancellationToken cancel = default)
        {
            SaveFailed?.Invoke(this, "the disk is full");
            return Task.CompletedTask;
        }
    }

    /// <summary>A store that breaks the rules by throwing instead of reporting.</summary>
    private sealed class ExplodingStore : IRoomStore
    {
        public event EventHandler<string>? SaveFailed
        {
            add { }
            remove { }
        }

        public List<Room> Load() => [new Room("Room 1")];

        public Task SaveAsync(IEnumerable<Room> rooms, CancellationToken cancel = default)
            => throw new IOException("the disk is gone");
    }

    /// <summary>A file in the temporary folder, deleted when the test ends.</summary>
    private sealed class TempRoomFile : IDisposable
    {
        public TempRoomFile(string? contents = null)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lantern-rooms-{Guid.NewGuid():N}.json");

            if (contents is not null)
            {
                File.WriteAllText(Path, contents);
            }
        }

        public string Path { get; }

        public void Dispose()
        {
            foreach (var candidate in new[] { Path, Path + ".tmp" })
            {
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
        }
    }
}
