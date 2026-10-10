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

    // ---------------------------------------------------- hearing it, or saying why nobody can

    [Fact]
    public void ASoundThatIsNotThereIsSaidOutLoudOnce()
    {
        // The file did not come with the program - a folder copied without its Media subfolder, which is exactly
        // what happens when someone takes "just the exe" to the lab. From the chair it is indistinguishable from
        // a muted machine, and only one of those two is fixable, so the window says which it thinks it is.
        var empty = Path.Combine(Path.GetTempPath(), "lantern-no-sounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);

        var previousDirectory = SoundNotifier.SoundDirectory;
        var previousMute = SoundNotifier.Muted;
        var said = new List<string>();

        try
        {
            SoundNotifier.SoundDirectory = empty;
            SoundNotifier.Muted = false;   // a test runner keeps the suite quiet; this one is about being heard

            using var sounds = new SoundNotifier();
            sounds.CouldNotPlay += said.Add;

            sounds.MessageArrived(Text());
            sounds.MessageArrived(Text());
            sounds.FileDone(Transfer());

            // Once, not per message: the reason does not change between one message and the next, and a bar that
            // repeated itself would be worse than a bar that says nothing.
            Assert.Single(said);
            Assert.Contains("message.wav", said[0], StringComparison.Ordinal);
            Assert.Contains("not next to Lantern", said[0], StringComparison.Ordinal);
            Assert.Equal(said[0], sounds.WhySilent);
        }
        finally
        {
            SoundNotifier.SoundDirectory = previousDirectory;
            SoundNotifier.Muted = previousMute;
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void ASoundTheMachineCannotMakeIsSaidOutLoudOnce()
    {
        // No sound card, a session with no window station, a wav truncated by a bad copy. The message still
        // arrives - that part of the behaviour is tested below and has not changed - but the reason the room is
        // silent stops being invisible.
        var said = new List<string>();
        using var sounds = new SoundNotifier(_ => throw new InvalidOperationException("no audio in this session"));
        sounds.CouldNotPlay += said.Add;

        sounds.MessageArrived(Text());
        sounds.FileDone(Transfer());

        Assert.Single(said);
        Assert.Contains("no audio in this session", said[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ANotifierThatCanBeNullSaysNothingAboutIt()
    {
        // A bar that announced "sound working" every time a message arrived would be the same noise as a bar
        // that never says anything, and twice as annoying.
        using var sounds = new SoundNotifier(_ => { });
        string? said = null;
        sounds.CouldNotPlay += reason => said = reason;

        sounds.MessageArrived(Text());

        Assert.Null(said);
        Assert.Null(sounds.WhySilent);
    }

    [Fact]
    public void AWindowThatCannotBeHeardSaysSoInItsBar()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            form.Show();

            using var sounds = new SoundNotifier(_ => throw new InvalidOperationException("no audio"));
            form.AttachSounds(sounds);

            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "hello", DateTime.UtcNow));

            Assert.Contains("no sound", Bar(form), StringComparison.Ordinal);
            Assert.Contains("no audio", Bar(form), StringComparison.Ordinal);

            form.Dispose();
        });
    }

    // ------------------------------------------------------------------ the files themselves

    [Fact]
    public void TheTwoSoundsAreLoudEnoughAndLongEnoughToHear()
    {
        // Nothing in this suite has ears: every other test here counts cues, which is why the first pair of
        // files passed every test while being a twelfth of a second at a fifth of full scale - inaudible across
        // a lab, which is the only room this feature is for. So the files are measured, not trusted.
        var message = Listen(SoundCue.Message);
        var ready = Listen(SoundCue.FileReady);

        // SoundPlayer plays uncompressed PCM and nothing else; anything else throws, which the notifier now
        // reports - and which would have been silently unfunny before.
        Assert.Equal("PCM", message.Format);
        Assert.Equal("PCM", ready.Format);
        Assert.Equal(16, message.Bits);

        Assert.True(message.Seconds >= 0.2, $"{message.Seconds:F2}s is too short to notice: {message.Path}");
        Assert.True(ready.Seconds >= 0.2, $"{ready.Seconds:F2}s is too short to notice: {ready.Path}");

        // Half of full scale at the loudest point. Not for loudness for its own sake: the peak is what survives
        // a laptop speaker in a room where four people are talking.
        Assert.True(message.Loudest >= 0.5, $"peak {message.Loudest:F2} is inaudible: {message.Path}");
        Assert.True(ready.Loudest >= 0.5, $"peak {ready.Loudest:F2} is inaudible: {ready.Path}");

        // Two cues a person can tell apart with their back turned, because the whole point of the second one is
        // that you do not have to look at the screen to know the file arrived.
        Assert.True(ready.Seconds > message.Seconds * 1.2,
            "the file cue is the longer one; two sounds of the same length are one sound twice");
    }

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

    // ------------------------------------------------------------------------- reading the files

    /// <summary>
    /// What a file is actually worth as a signal: how long it sounds for, how loud its loudest moment is against
    /// full scale, and whether it is in a format Windows will play at all.
    /// </summary>
    private static (string Path, double Seconds, double Loudest, string Format, int Bits) Listen(SoundCue cue)
    {
        var path = SoundNotifier.PathFor(cue);
        var bytes = File.ReadAllBytes(path);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));

        var format = "?";
        var bits = 0;
        var channels = 0;
        var rate = 0;
        var start = 0;
        var length = 0;

        var at = 12;
        while (at + 8 <= bytes.Length)
        {
            var chunk = System.Text.Encoding.ASCII.GetString(bytes, at, 4);
            var size = BitConverter.ToInt32(bytes, at + 4);

            if (chunk == "fmt ")
            {
                format = BitConverter.ToInt16(bytes, at + 8) switch
                {
                    1 => "PCM",
                    2 => "ADPCM",
                    3 => "IEEE float",
                    0xff => "extensible",
                    _ => "unknown",
                };
                channels = BitConverter.ToInt16(bytes, at + 10);
                rate = BitConverter.ToInt32(bytes, at + 12);
                bits = BitConverter.ToInt16(bytes, at + 22);
            }
            else if (chunk == "data")
            {
                start = at + 8;
                length = size;
            }

            at += 8 + size + (size % 2);
        }

        Assert.True(rate > 0 && start > 0, $"{path} is not a wav this program can look at");
        Assert.InRange(channels, 1, 2);

        var frames = length / (bits / 8);
        var loudest = 0.0;
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = Math.Abs(BitConverter.ToInt16(bytes, start + frame * 2) / (double)short.MaxValue);
            loudest = Math.Max(loudest, sample);
        }

        return (path, frames / (double)rate, loudest, format, bits);
    }

    /// <summary>The status strip's label is a tool strip item, not a control, so it is not in Controls.</summary>
    private static string Bar(Control form)
    {
        var strip = (StatusStrip)form.Controls.Find("statusStrip", searchAllChildren: true)[0];
        return strip.Items.Cast<ToolStripItem>().Single(item => item.Name == "lblStatus").Text ?? string.Empty;
    }

    private static TextMessage Text() => new("Nadia", "Room 1", "hello", DateTime.UtcNow);

    private static FileTransferJob Transfer()
    {
        var job = new FileTransferJob("notes.zip", 4, senderNickname: "Nadia", roomName: "Room 1");
        job.AddBytes(4);
        return job;
    }
}
