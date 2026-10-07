using System.Drawing.Imaging;
using Lantern.Domain;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// The domain classes of Figure 7 (T1.1, T1.2, T1.3), tested without a form or a socket.
/// </summary>
public class DomainModelTests
{
    [Fact]
    public void EveryMessageCarriesItsOwnRoom()
    {
        var text = new TextMessage("Haitao", "Room 1", "hi");
        var notice = SystemMessage.Join("Room 1", "Victory");
        var file = new FileMessage("Haitao", "Room 2", "notes.txt", 2048);
        var picture = new PictureMessage("Haitao", "Room 3", "a.png", [1, 2, 3]);

        Assert.Equal(MessageKind.TEXT, text.Kind);
        Assert.Equal(MessageKind.JOIN_LEAVE_NOTICE, notice.Kind);
        Assert.Equal(MessageKind.FILE, file.Kind);
        Assert.Equal(MessageKind.PICTURE, picture.Kind);

        Assert.Equal("Room 1", text.RoomName);
        Assert.Equal("Room 2", file.RoomName);
        Assert.Equal("Room 3", picture.RoomName);

        Assert.All(new Message[] { text, notice, file, picture }, m =>
        {
            Assert.NotEqual(Guid.Empty, m.Id);
            Assert.NotEqual(default, m.SentAt);
        });

        Assert.NotEqual(text.Id, new TextMessage("Haitao", "Room 1", "hi").Id);
    }

    [Fact]
    public void PreviewIsTheOneLineVersionOfAMessage()
    {
        Assert.Equal("hi", new TextMessage("Haitao", "Room 1", "hi").Preview);
        Assert.Equal("Victory - join this chat", SystemMessage.Join("Room 1", "Victory").Preview);
        Assert.Equal("Victory - left this chat", SystemMessage.Leave("Room 1", "Victory").Preview);
        Assert.Equal("Victory - was removed from this chat", SystemMessage.Kick("Room 1", "Victory").Preview);
        Assert.Contains("notes.txt", new FileMessage("Haitao", "Room 1", "notes.txt", 2048).Preview);
        Assert.Contains("a.png", new PictureMessage("Haitao", "Room 1", "a.png", [1, 2, 3]).Preview);
    }

    [Fact]
    public void SystemNoticesHaveNoSender()
    {
        var notice = SystemMessage.Join("Room 1", "Victory");

        Assert.Equal(string.Empty, notice.SenderNickname);
    }

    [Fact]
    public void AFileMessagePointsAtOneTransfer()
    {
        var first = new FileMessage("Haitao", "Room 1", "notes.txt", 10);
        var second = new FileMessage("Haitao", "Room 1", "notes.txt", 10);
        var announced = new Guid("11111111-1111-1111-1111-111111111111");

        Assert.NotEqual(first.TransferId, second.TransferId);
        Assert.Equal(announced, new FileMessage("Haitao", "Room 1", "notes.txt", 10, announced).TransferId);
    }

    [Fact]
    public void APictureScalesToAThumbnailAndRefusesJunk()
    {
        byte[] png;
        using (var bitmap = new Bitmap(400, 200))
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            png = ms.ToArray();
        }

        var picture = new PictureMessage("Haitao", "Room 1", "big.png", png);
        var thumbnail = picture.ToThumbnail();

        try
        {
            Assert.NotNull(thumbnail);
            Assert.Equal(PictureMessage.ThumbnailEdge, Math.Max(thumbnail!.Width, thumbnail.Height));
            Assert.Equal(PictureMessage.ThumbnailEdge / 2, thumbnail.Height);
        }
        finally
        {
            thumbnail?.Dispose();
        }

        Assert.Null(new PictureMessage("Haitao", "Room 1", "junk.png", [9, 8, 7]).ToThumbnail());
    }

    [Fact]
    public void AUserIsIdentifiedByItsConnection()
    {
        var a = new User("conn-1", "Haitao");
        var same = new User("conn-1", "Haitao");
        var otherConnection = new User("conn-2", "Haitao");

        Assert.Equal(a, same);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(a, otherConnection);
        Assert.Equal("Haitao", a.ToString());

        Assert.Throws<ArgumentException>(() => new User("  ", "Haitao"));
        Assert.Throws<ArgumentException>(() => new User("conn-1", "   "));
    }

    [Fact]
    public void NicknamesAndRoomNamesAreNormalisedOnEntry()
    {
        Assert.Equal("Haitao Wu", new User("c", "  Haitao   Wu ").Nickname);
        Assert.Equal("Room 1", new Room("  Room   1  ").Name);

        Assert.Throws<ArgumentException>(() => new Room("   "));
    }

    [Fact]
    public void RoomsHoldMembersWithoutDoubles()
    {
        var room = new Room("Room 1");
        var changed = 0;
        room.MembersChanged += (_, _) => changed++;

        Assert.True(room.AddMember(new User("c1", "Haitao")));
        Assert.True(room.AddMember(new User("c2", "Victory")));
        Assert.False(room.AddMember(new User("c1", "Someone else")));

        Assert.True(room.HasMember("c2"));
        Assert.Equal(2, room.Members.Count);
        Assert.Equal(2, changed);

        Assert.True(room.RemoveMember(new User("c2", "Victory")));
        Assert.False(room.RemoveMember(new User("c9", "Nobody")));

        room.ClearMembers();
        Assert.Empty(room.Members);
    }

    [Fact]
    public void ARoomKeepsOnlyItsOwnMessages()
    {
        var room = new Room("Room 1");
        Message? appended = null;
        room.MessageAppended += (_, message) => appended = message;

        room.Append(new TextMessage("Haitao", "Room 1", "hello"));
        Assert.Throws<ArgumentException>(() => room.Append(new TextMessage("Haitao", "Room 2", "wrong room")));

        Assert.Equal("hello", Assert.IsType<TextMessage>(appended).Body);
        Assert.Single(room.Transcript);
    }

    [Fact]
    public void TheTranscriptIsASnapshotNotALiveView()
    {
        var room = new Room("Room 1");
        room.Append(new TextMessage("Haitao", "Room 1", "one"));

        var snapshot = room.Transcript;
        room.Append(new TextMessage("Haitao", "Room 1", "two"));

        Assert.Single(snapshot);
        Assert.Equal(2, room.Transcript.Count);
    }

    [Fact]
    public void ATenThousandAdditionsToOneRoomStayConsistent()
    {
        // The wire layer feeds rooms from several read loops at once, so Append must not corrupt
        // the transcript when two threads race.
        var room = new Room("Room 1");

        Parallel.For(0, 10_000, i => room.Append(new TextMessage("Haitao", "Room 1", $"m{i}")));

        Assert.Equal(10_000, room.Transcript.Count);
    }

    [Fact]
    public void ATransferJobTracksProgress()
    {
        var job = new FileTransferJob("notes.txt", 100, senderNickname: "Victory", roomName: "Room 1");
        var progress = new CountingProgress();
        job.Progress = progress;

        Assert.False(job.IsComplete);
        Assert.Equal(0, job.Fraction);

        job.AddBytes(40);
        Assert.Equal(40, job.BytesDone);
        Assert.Equal(0.4, job.Fraction, 3);
        Assert.False(job.IsComplete);
        Assert.Equal(1, progress.Count);

        job.AddBytes(100);
        Assert.Equal(100, job.BytesDone);
        Assert.True(job.IsComplete);

        Assert.Throws<ArgumentOutOfRangeException>(() => job.AddBytes(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileTransferJob("x", -5));
        Assert.Throws<ArgumentException>(() => new FileTransferJob("  ", 5));
    }

    [Fact]
    public void ATransferJobOnlyEverKeepsAFileName()
    {
        // A peer must never be able to dictate a path on this machine.
        var job = new FileTransferJob(@"C:\evil\..\..\windows\system32\notes.txt", 10);

        Assert.Equal("notes.txt", job.FileName);
    }

    [Fact]
    public void AnEmptyFileIsCompleteFromTheStart()
    {
        // A receiving loop shaped "while (!job.IsComplete) read bytes" must fall straight through
        // for a zero-length file instead of waiting for bytes that will never arrive.
        var job = new FileTransferJob("empty", 0);

        Assert.True(job.IsComplete);
        Assert.Equal(1, job.Fraction);
    }

    [Fact]
    public void ATransferJobCanBeCancelledOnce()
    {
        var job = new FileTransferJob("notes.txt", 100);

        Assert.False(job.CancellationToken.IsCancellationRequested);

        job.CancelTransfer();
        job.CancelTransfer();

        Assert.True(job.CancellationToken.IsCancellationRequested);
        Assert.True(job.Cancel.IsCancellationRequested);
    }

    /// <summary>IProgress that reports synchronously, unlike Progress&lt;T&gt;, which posts to a context.</summary>
    private sealed class CountingProgress : IProgress<double>
    {
        public int Count { get; private set; }

        public void Report(double value) => Count++;
    }
}
