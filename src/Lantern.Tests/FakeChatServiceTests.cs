using Lantern.Core;
using Lantern.Domain;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// The in-memory service is what the windows run on until the wire exists (M2), so its own
/// behaviour — rooms, membership, transcript and the events the UI listens to — is under test here.
/// </summary>
public class FakeChatServiceTests
{
    [Fact]
    public void StartsOutOfTheWireframeShape()
    {
        var service = new FakeChatService("Haitao");

        Assert.Equal("Haitao", service.Nickname);
        Assert.True(service.IsHost);
        Assert.Equal(["Room 1", "Room 2", "Room 3"], service.Rooms.Select(r => r.Name));
        Assert.All(service.Rooms.Skip(1), room => Assert.Empty(room.Members));
    }

    [Fact]
    public async Task AddingARoomRaisesRoomsChanged()
    {
        var service = new FakeChatService("Haitao");
        var fired = 0;
        service.RoomsChanged += (_, _) => fired++;

        await service.CreateRoomAsync("Announced later");

        Assert.Equal(1, fired);
        Assert.Contains("Announced later", service.Rooms.Select(r => r.Name));
    }

    [Fact]
    public async Task JoiningARoomFillsMembersAndRaisesMembersChanged()
    {
        var service = new FakeChatService("Victory", isHost: false);
        var fired = 0;
        service.MembersChanged += (_, _) => fired++;

        await service.JoinByNameAsync("Room 2");

        Assert.Contains(service.Members, u => u.Nickname == "Victory");
        Assert.True(fired > 0);
    }

    [Fact]
    public async Task LeavingByJoiningNothingClearsMembers()
    {
        var service = new FakeChatService("Victory", isHost: false);
        await service.JoinByNameAsync("Room 1");

        await service.JoinByNameAsync(string.Empty);

        Assert.Empty(service.Members);
    }

    [Fact]
    public async Task JoiningAndLeavingAreAnnouncedInTheTranscript()
    {
        var service = new FakeChatService("Victory", isHost: false);

        await service.JoinByNameAsync("Room 2");
        await service.JoinByNameAsync(string.Empty);

        var notices = service.Transcript.OfType<SystemMessage>().Select(m => m.Preview).ToArray();
        Assert.Contains(notices, n => n.Contains(SystemMessage.JoinSuffix));
        Assert.Contains(notices, n => n.Contains(SystemMessage.LeaveSuffix));
    }

    [Fact]
    public async Task SendingRecordsTheMessageInTheRoomTranscript()
    {
        var service = new FakeChatService("Haitao");
        await service.JoinByNameAsync("Room 1");

        await service.SendAsync(new TextMessage("Haitao", "Room 1", "hello"));

        Assert.Contains("hello", service.Rooms[0].Transcript.OfType<TextMessage>().Select(m => m.Body));
        Assert.Contains("hello", service.Transcript.OfType<TextMessage>().Select(m => m.Body));
    }

    [Fact]
    public void IncomingTrafficRaisesMessageReceived()
    {
        var service = new FakeChatService("Haitao");
        Message? received = null;
        service.MessageReceived += (_, message) => received = message;

        service.SimulateIncoming(new TextMessage("User 2", "Room 1", "from the wire"));

        Assert.NotNull(received);
        Assert.Equal("from the wire", Assert.IsType<TextMessage>(received).Body);
    }

    [Fact]
    public async Task ClientsCannotEditRooms()
    {
        var service = new FakeChatService("Victory", isHost: false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.CreateRoomAsync("Nope"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.DeleteByNameAsync("Room 1"));
    }

    [Fact]
    public async Task DuplicateRoomNamesAreRefused()
    {
        var service = new FakeChatService("Haitao");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.CreateRoomAsync("Room 1"));

        Assert.Contains("already exists", error.Message);
    }

    [Fact]
    public async Task RenamingKeepsTheMembers()
    {
        var service = new FakeChatService("Haitao");

        await service.RenameByNameAsync("Room 1", "Renamed");

        Assert.Contains("Renamed", service.Rooms.Select(r => r.Name));
        Assert.Equal(3, service.Rooms.Single(r => r.Name == "Renamed").Members.Count);
    }

    [Fact]
    public async Task DeletingARoomTakesItOutOfTheList()
    {
        var service = new FakeChatService("Haitao");

        await service.DeleteByNameAsync("Room 3");

        Assert.DoesNotContain("Room 3", service.Rooms.Select(r => r.Name));
    }

    [Fact]
    public async Task IncomingFileIsAnnouncedAsAJob()
    {
        var service = new FakeChatService("Haitao");
        await service.JoinByNameAsync("Room 1");
        FileTransferJob? announced = null;
        service.IncomingFile += (_, job) => announced = job;

        service.SimulateIncomingFile("notes.txt", 2048);

        Assert.NotNull(announced);
        Assert.Equal("notes.txt", announced!.FileName);
        Assert.Equal(2048, announced.SizeBytes);
        Assert.Equal("Room 1", announced.RoomName);
    }

    [Fact]
    public async Task SavingAnIncomingFileWaitsForItAndThenWritesIt()
    {
        var service = new FakeChatService("Haitao");
        var folder = Path.Combine(Path.GetTempPath(), "lantern-t54-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "notes.txt");

        try
        {
            var job = new FileTransferJob("notes.txt", 4096, senderNickname: "User 2", roomName: "Room 1");

            var saving = service.SaveReceivedFileAsync(job, path);

            // Being told where to put a file is not having it. Nothing is written until the last byte turns
            // up, which is the difference between "saved" and "on its way" - and a fake that pretended the
            // two were the same would let a window report a file that was never whole.
            await Task.Delay(150);
            Assert.False(File.Exists(path));

            service.AdvanceFile(job, 4096);

            await saving;

            Assert.True(job.IsComplete);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task AFileThatNeverArrivedWholeIsNotWritten()
    {
        var service = new FakeChatService("Haitao");
        var folder = Path.Combine(Path.GetTempPath(), "lantern-t54-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "notes.txt");

        try
        {
            var job = new FileTransferJob("notes.txt", 4096, senderNickname: "User 2", roomName: "Room 1");

            var saving = service.SaveReceivedFileAsync(job, path);

            service.AdvanceFile(job, 100);
            service.SimulateFileStopped(job);

            // A half file with the real file's name on it is worse than no file at all, because it is the
            // sort of thing that gets opened a week later.
            await Assert.ThrowsAsync<InvalidOperationException>(() => saving);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ConnectionLossIsReportedAsAnError()
    {
        var service = new FakeChatService("Victory", isHost: false);
        Exception? reported = null;
        service.ConnectionLost += (_, error) => reported = error;

        service.SimulateConnectionLost();

        Assert.NotNull(reported);
    }

    [Fact]
    public async Task ShutdownEmptiesEveryRoom()
    {
        var service = new FakeChatService("Haitao");

        await service.ShutdownAsync();

        Assert.All(service.Rooms, room => Assert.Empty(room.Members));
        Assert.Empty(service.Members);
    }
}
