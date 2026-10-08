using Lantern.Core;
using Lantern.Domain;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T5.5 / F10: the room makes a noise when something arrives in it. Two sounds rather than one, because
/// "somebody said something" and "the file is here" are not the same reason to look up - and because a noise
/// that goes off for things that failed is a noise that gets switched off within a day.
/// </summary>
public sealed class SoundNotifierTests
{
    [Fact]
    public void AMessagesMakesTheMessageNoise()
    {
        var heard = new List<SoundCue>();
        using var sounds = new SoundNotifier(heard.Add);

        sounds.MessageArrived(new TextMessage("Nadia", "Room 1", "hello", DateTime.UtcNow));

        Assert.Equal(new[] { SoundCue.Message }, heard);
    }

    [Fact]
    public void APictureAndANewcomerAreWorthANoiseToo()
    {
        var heard = new List<SoundCue>();
        using var sounds = new SoundNotifier(heard.Add);

        sounds.MessageArrived(new PictureMessage("Nadia", "Room 1", "sunset.png", new byte[16]));
        sounds.MessageArrived(new SystemMessage("Room 1", "Dov joined the room", DateTime.UtcNow));

        Assert.Equal(2, heard.Count);
        Assert.All(heard, cue => Assert.Equal(SoundCue.Message, cue));
    }

    [Fact]
    public void AnnouncingAFileIsNotTheSameAsHavingIt()
    {
        var heard = new List<SoundCue>();
        using var sounds = new SoundNotifier(heard.Add);

        // The transcript line for a file arrives with FILE_META, and the bytes take however long they take.
        // Noising at both ends of that would be two noises for one delivery.
        sounds.MessageArrived(new FileMessage("Nadia", "Room 1", "notes.zip", 4096));

        Assert.Empty(heard);
    }

    [Fact]
    public void AFileThatArrivedMakesADifferentNoiseToAMessage()
    {
        var heard = new List<SoundCue>();
        using var sounds = new SoundNotifier(heard.Add);

        var job = new FileTransferJob("notes.zip", 8, senderNickname: "Nadia", roomName: "Room 1");
        job.AddBytes(8);

        sounds.FileDone(job);

        Assert.Equal(new[] { SoundCue.FileReady }, heard);
        Assert.NotEqual(SoundCue.Message, heard[0]);
    }

    [Fact]
    public void AFileThatDidNotFinishMakesNoNoiseAtAll()
    {
        var heard = new List<SoundCue>();
        using var sounds = new SoundNotifier(heard.Add);

        var job = new FileTransferJob("notes.zip", 8, senderNickname: "Nadia", roomName: "Room 1");
        job.AddBytes(3);
        job.CancelTransfer();

        // Stopped is not finished. A cheerful sound after a failed transfer is the sort of thing that gets a
        // person switching the sound off altogether - which costs them every message after it.
        sounds.FileDone(job);

        Assert.Empty(heard);
    }

    [Fact]
    public void AWindowNobodyAskedToBeNoisySaysNothing()
    {
        var heard = new List<SoundCue>();
        using var sounds = new SoundNotifier(heard.Add) { Enabled = false };

        sounds.MessageArrived(new TextMessage("Nadia", "Room 1", "hello", DateTime.UtcNow));
        sounds.FileDone(new FileTransferJob("notes.zip", 0, senderNickname: "Nadia", roomName: "Room 1"));

        Assert.Empty(heard);
    }

    [Fact]
    public void ABrokenSoundCardDoesNotBreakTheChat()
    {
        using var sounds = new SoundNotifier(_ => throw new InvalidOperationException("no audio in this session"));

        // Nothing thrown back at the message loop, and nothing to show the person: the message arrived, which
        // is the part they asked for.
        sounds.MessageArrived(new TextMessage("Nadia", "Room 1", "hello", DateTime.UtcNow));

        var job = new FileTransferJob("notes.zip", 4, senderNickname: "Nadia", roomName: "Room 1");
        job.AddBytes(4);
        sounds.FileDone(job);
    }

    [Fact]
    public void TheTwoSoundsAreFilesNextToTheProgram()
    {
        // Not embedded: whoever uses this may want different sounds, and replacing a wav is a thing a person
        // can do for themselves. Two of them, and they are not the same file, which is the "distinct sound"
        // the task asks for.
        var message = SoundNotifier.PathFor(SoundCue.Message);
        var ready = SoundNotifier.PathFor(SoundCue.FileReady);

        Assert.NotEqual(message, ready);
        Assert.True(File.Exists(message), $"expected {message}");
        Assert.True(File.Exists(ready), $"expected {ready}");

        Assert.Equal("wav", Path.GetExtension(ready).TrimStart('.').ToLowerInvariant());
    }

    [Fact]
    public void AWindowToldToMakeANoiseBeepsWhenSomethingArrives()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var heard = new List<SoundCue>();

            form.Show();
            form.AttachSounds(new SoundNotifier(heard.Add));

            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "hello", DateTime.UtcNow));

            // The window's own message does not arrive this way - nothing echoes it back - so the only
            // messages that can reach here are somebody else's.
            Assert.Equal(new[] { SoundCue.Message }, heard);

            form.Dispose();
        });
    }

    [Fact]
    public Task AWindowMakesTheFileNoiseWhenAFileFinishes()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var heard = new List<SoundCue>();

            form.Show();
            form.AttachSounds(new SoundNotifier(heard.Add));

            var job = service.SimulateIncomingFile("notes.zip", 4096);

            // Not yet: the announcement is on the screen, but the file is not here.
            await Task.Delay(100);
            Assert.Empty(heard);

            service.AdvanceFile(job, 4096);

            await Watch.UntilAsync(
                () => heard.Contains(SoundCue.FileReady),
                "the window to make the file noise once the file was whole");

            // One noise for the delivery, whatever else the room said about it.
            Assert.Equal(new[] { SoundCue.FileReady }, heard);

            form.Dispose();
        });

    [Fact]
    public Task AFileStoppedOnTheWayMakesNoNoise()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var heard = new List<SoundCue>();

            form.Show();
            form.AttachSounds(new SoundNotifier(heard.Add));

            var job = service.SimulateIncomingFile("notes.zip", 4096);
            service.AdvanceFile(job, 500);
            service.SimulateFileStopped(job);

            await Watch.UntilAsync(() => form.FileProgressPercent == -1, "the window to give up on the file");

            Assert.DoesNotContain(SoundCue.FileReady, heard);

            form.Dispose();
        });

    [Fact]
    public void AWindowDoesNotGetTwoNotifiers()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            form.Show();

            using var first = new SoundNotifier(_ => { });
            using var second = new SoundNotifier(_ => { });

            form.AttachSounds(first);

            // Two notifiers would mean two noises per message, and the second one arriving is a wiring
            // mistake rather than a choice anybody made on purpose.
            Assert.Throws<InvalidOperationException>(() => form.AttachSounds(second));

            form.Dispose();
        });
    }
}
