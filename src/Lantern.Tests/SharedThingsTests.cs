using Lantern.Core;
using Lantern.Domain;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// v1.2: the two things a person asks of a shared thing once it is in a room.
/// <para>
/// "Where is it on MY machine?" - a file being received already asked where it should go, once, in the moment it
/// arrived; a picture never asked at all. Both live in the transcript afterwards, which is where somebody looking
/// for them an hour later will be, so that is where the asking happens now.
/// </para>
/// <para>
/// "Is it still going?" - a file had a bar but said only a name and a percentage, which answers neither of the two
/// questions a bar is asked (which way, and how much); and a picture, whose bytes travel in a single frame and so
/// has no percentage to give, showed nothing at all for the minute a five megabyte photo takes over a radio.
/// </para>
/// </summary>
public sealed class SharedThingsTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    [Fact]
    public Task AFileOnTheWaySaysWhichWayItIsGoingAndHowMuchOfIt()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao")
            {
                FilesCompleteImmediately = false,
            };

            using var form = new Sharing(service);
            var file = Write("deck.pdf", 40_000);

            try
            {
                form.Show();
                Rooms(form).SelectedIndex = 0;
                form.Answer = file;

                Click(form, "btnShare");

                await Watch.UntilAsync(() => form.FileProgressPercent >= 0, "the bar to appear");

                // Which way it is going comes first, because that is the question a person staring at a bar
                // actually has. A name and a percentage on their own answer neither of the two questions a bar
                // is asked.
                Assert.StartsWith("sending deck.pdf", form.FileProgressText, StringComparison.Ordinal);

                // How much of it, in bytes as well as per cent: over a radio "34%" of a file whose size was
                // never mentioned tells you nothing about how long the remaining two thirds will take.
                Assert.Contains("%", form.FileProgressText, StringComparison.Ordinal);
                Assert.Contains("of", form.FileProgressText, StringComparison.Ordinal);
                Assert.DoesNotContain("receiving", form.FileProgressText, StringComparison.Ordinal);
            }
            finally
            {
                Delete(file);
            }
        });

    [Fact]
    public void AFileComingInSaysItIsComingIn()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao")
            {
                FilesCompleteImmediately = false,
            };

            using var form = new Sharing(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var job = service.SimulateIncomingFile("notes.txt", 40_000);
            service.AdvanceFile(job, 10_000);

            Assert.StartsWith("receiving notes.txt", form.FileProgressText, StringComparison.Ordinal);
            Assert.Contains("of", form.FileProgressText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public Task APictureThatIsStillGoingSaysSo()
        => Sta.RunAsync(async () =>
        {
            var service = new SlowSender("Haitao");
            using var form = new Sharing(service);
            var photo = Write("sunset.png", OnePixelPng);

            try
            {
                form.Show();
                Rooms(form).SelectedIndex = 0;
                form.Answer = photo;

                Click(form, "btnShare");

                // A picture has no percentage to give - its bytes are one frame, and there is no counting the
                // middle of a frame. A bar that moves and a count of the seconds is what can honestly be said,
                // and it is the difference between a slow window and a dead one.
                await Watch.UntilAsync(() => form.IsProgressRunningBlind, "a bar to move for the picture");

                Assert.Contains("sending sunset.png", form.FileProgressText, StringComparison.Ordinal);
                Assert.Matches(@"\d+ s", form.FileProgressText);

                // No Stop button: half of one frame is not a thing the wire can do, and a button that cannot
                // stop anything is worse than no button.
                Assert.False(form.CanStopFile);

                service.Release();

                await Watch.UntilAsync(
                    () => !form.IsProgressRunningBlind && form.FileProgressPercent == -1,
                    "the picture's bar to come down when it has gone");
            }
            finally
            {
                Delete(photo);
            }
        });

    [Fact]
    public Task ASharedPictureCanBePutInAFolderOfOnesOwn()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Sharing(service);
            var folder = NewFolder();

            form.Show();
            Rooms(form).SelectedIndex = 0;

            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", OnePixelPng));

            var text = Transcript(form);
            Assert.True(form.CanSaveSharedAt(text.IndexOf("sunset.png", StringComparison.Ordinal)),
                "a picture in the transcript is something a right click can do something about");

            form.Folder = folder;
            var saved = await form.SaveSharedAtAsync(text.IndexOf("sunset.png", StringComparison.Ordinal));

            Assert.NotNull(saved);
            Assert.Equal(OnePixelPng, File.ReadAllBytes(saved!));

            // What was sent, not what the thumbnail was shrunk to: the point of saving a photo is the photo.
            Assert.Equal("sunset.png", Path.GetFileName(saved));
            Assert.Contains("saved", form.StatusText, StringComparison.Ordinal);
        });

    [Fact]
    public Task SavingAPictureTwiceDoesNotDestroyTheFirstCopy()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Sharing(service);
            var folder = NewFolder();

            form.Show();
            Rooms(form).SelectedIndex = 0;

            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", OnePixelPng));
            var caret = Transcript(form).IndexOf("sunset.png", StringComparison.Ordinal);

            form.Folder = folder;
            var first = await form.SaveSharedAtAsync(caret);
            var second = await form.SaveSharedAtAsync(caret);

            // A folder that already has that name gets a second file with a number on it, not a photo replaced.
            // A chat window has no business quietly deleting something the person never mentioned.
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotEqual(first, second);
            Assert.Equal(2, Directory.GetFiles(folder).Length);
        });

    [Fact]
    public void AFileThatArrivedCanBeSavedFromTheTranscriptAfterwards()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Sharing(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var job = service.SimulateIncomingFile("notes.txt", 4_096);
            service.AdvanceFile(job, 4_096);

            var caret = Transcript(form).IndexOf("notes.txt", StringComparison.Ordinal);

            // The offer strip comes down as soon as the file is answered; the entry stays. "I want that in my
            // Documents folder after all" is asked of the transcript, not of a strip that has gone away.
            Assert.True(form.CanSaveSharedAt(caret));
        });
    }

    [Fact]
    public void AFileThisWindowSentIsNotOfferedBackToIt()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Sharing(service);
            var file = Write("deck.pdf", 2_000);

            try
            {
                form.Show();
                Rooms(form).SelectedIndex = 0;
                form.Answer = file;

                Click(form, "btnShare");

                var caret = Transcript(form).IndexOf("deck.pdf", StringComparison.Ordinal);
                Assert.True(caret >= 0);

                // It is already on this machine - offering to save a file back onto itself is a menu item that
                // does nothing, and the person has to find that out by clicking it.
                Assert.False(form.CanSaveSharedAt(caret));

                // And an ordinary sentence offers nothing either: right-clicking words shows no menu at all.
                service.SimulateIncoming(new TextMessage("User 2", "Room 1", "hello there"));
                Assert.False(form.CanSaveSharedAt(
                    Transcript(form).IndexOf("hello there", StringComparison.Ordinal)));
            }
            finally
            {
                Delete(file);
            }
        });
    }

    [Fact]
    public Task AFileAlreadySavedSaysWhereItWentRatherThanSavingAgain()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Sharing(service);
            var folder = NewFolder();

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var job = service.SimulateIncomingFile("notes.txt", 4_096);
            service.AdvanceFile(job, 4_096);

            var caret = Transcript(form).IndexOf("notes.txt", StringComparison.Ordinal);

            // First time: the offer strip, which is what has always asked.
            form.Folder = folder;
            Click(form, "btnFileSave");
            await Watch.UntilAsync(() => File.Exists(Path.Combine(folder, "notes.txt")), "the file to be written");

            await TheWindowCatchingUpAsync();

            // The second request is answered with where the file actually is, rather than by writing another
            // copy and saying nothing about either.
            var again = await form.SaveSharedAtAsync(caret);
            await TheWindowCatchingUpAsync();

            Assert.NotNull(again);
            Assert.Contains("already saved at", form.StatusText, StringComparison.Ordinal);
            Assert.Single(service.SaveRequests);
            Assert.Single(Directory.GetFiles(folder));
        });

    // ------------------------------------------------------------------ harness

    private sealed class Sharing : Lantern.UI.HostForm
    {
        public Sharing(IChatService service) : base(service)
        {
        }

        /// <summary>The folder this imaginary person chooses; null closes the box without choosing.</summary>
        public string? Folder;

        /// <summary>Their choice of something to share.</summary>
        public string? Answer;

        protected override string? AskForFolder(string fileName) => Folder;

        protected override string? AskForAttachment() => Answer;
    }

    /// <summary>A service whose sends do not finish until the test says so - a picture's bytes on a radio.</summary>
    private sealed class SlowSender : FakeChatService
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SlowSender(string nickname) : base(nickname)
        {
        }

        public void Release() => gate.SetResult();

        public override async Task SendAsync(Message message, CancellationToken cancel = default)
        {
            await base.SendAsync(message, cancel);
            await gate.Task;
        }
    }

    /// <summary>
    /// Runs the window's own queue for a moment.
    /// <para>
    /// A save finishes on a worker thread and posts its line back to the window. A test that only <c>await</c>s
    /// does not pump messages, so that posted line sits in the queue unread and an assertion about the status bar
    /// fails for a reason that has nothing to do with the program. The running program has a message loop; this
    /// is the harness not having one, and any test that wants to read what a finished operation said has to pump
    /// before it looks.
    /// </para>
    /// </summary>
    private static async Task TheWindowCatchingUpAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            Application.DoEvents();
            await Task.Delay(25);
        }
    }

    private static ListBox Rooms(Control form) => (ListBox)Find(form, "lstRooms")!;

    private static string Transcript(Control form) => ((RichTextBox)Find(form, "rtbChat")!).Text;

    private static void Click(Control form, string name) => ((Button)Find(form, name)!).PerformClick();

    private static Control? Find(Control parent, string name)
        => parent.Controls.Find(name, searchAllChildren: true).FirstOrDefault();

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lantern-v12-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Write(string name, int size)
    {
        var path = Path.Combine(NewFolder(), name);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    private static string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(NewFolder(), name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
        catch (IOException)
        {
            // Leftovers in the temp folder are untidy, not a failure.
        }
    }
}
