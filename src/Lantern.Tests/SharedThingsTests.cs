using System.Runtime.InteropServices;
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

    /// <summary>
    /// A file this window sent, saved again somewhere else.
    /// </summary>
    /// <remarks>
    /// v1.2 refused this deliberately - the bytes are already on this machine, so offering them back looked like a
    /// menu item that does nothing. The refusal was the bug: a right click that produces nothing at all cannot be
    /// told apart from a program that is broken, and it arrived as "right click file placeholder not working".
    /// What a person pointing at their own file wants is a copy in another folder - to email it, to put it on a
    /// stick - and Lantern can do that, because it remembers where it read the file from.
    /// </remarks>
    [Fact]
    public Task AFileThisWindowSentOffersACopyOfItself()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Sharing(service);
            var file = Write("deck.pdf", 2_000);
            var folder = NewFolder();

            try
            {
                form.Show();
                Rooms(form).SelectedIndex = 0;
                form.Answer = file;

                Click(form, "btnShare");

                var caret = Transcript(form).IndexOf("deck.pdf", StringComparison.Ordinal);
                Assert.True(caret >= 0);
                Assert.True(form.CanSaveSharedAt(caret));

                // It says "a copy", because that is what it is: the file that was sent stays where it is.
                var menu = RightClickOn(form, "deck.pdf");

                Assert.True(menu.Visible, "a file this window sent can be copied somewhere else");
                Assert.Equal("Save a copy to…", menu.Items[0].Text);

                menu.Close();

                form.Folder = folder;
                var saved = await form.SaveSharedAtAsync(caret);
                await TheWindowCatchingUpAsync();

                Assert.NotNull(saved);
                Assert.Equal(2_000, new FileInfo(saved!).Length);
                Assert.Contains("saved a copy of deck.pdf", form.StatusText, StringComparison.Ordinal);
            }
            finally
            {
                Delete(file);
            }
        });

    /// <summary>
    /// The same, when the file that was sent is no longer where it was.
    /// </summary>
    [Fact]
    public Task ACopyOfAFileThatHasGoneOneIsRefusedOutLoud()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Sharing(service);
            var file = Write("deck.pdf", 2_000);
            var folder = NewFolder();

            try
            {
                form.Show();
                Rooms(form).SelectedIndex = 0;
                form.Answer = file;

                Click(form, "btnShare");

                var caret = Transcript(form).IndexOf("deck.pdf", StringComparison.Ordinal);

                // The stick is out, the file is on the desktop no more. Lantern kept no copy of what it sent, so
                // this is the honest answer - and it names the file and the place, rather than doing nothing.
                Delete(file);

                form.Folder = folder;
                var saved = await form.SaveSharedAtAsync(caret);
                await TheWindowCatchingUpAsync();

                Assert.Null(saved);
                Assert.Contains("deck.pdf", form.StatusText, StringComparison.Ordinal);
                Assert.Contains("any more", form.StatusText, StringComparison.Ordinal);
                Assert.Empty(Directory.GetFiles(folder));
            }
            finally
            {
                Delete(file);
            }
        });

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

    /// <summary>
    /// A menu raised without the mouse follows the caret.
    /// </summary>
    /// <remarks>
    /// The offer used to hang entirely on where <see cref="Cursor"/> was at the moment the menu opened, which is a
    /// question a keyboard menu key, a touch and hold, and some touchpads do not answer. The control has a caret,
    /// and a right click puts it where the click happened, so the caret is asked as well - and this test asks it
    /// with the cursor parked off the screen, where no amount of pointing could have helped.
    /// </remarks>
    [Fact]
    public void AMenuRaisedWithoutTheMouseFollowsTheCaret()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Sharing(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var job = service.SimulateIncomingFile("notes.txt", 4_096);
            service.AdvanceFile(job, 4_096);

            var rtb = (RichTextBox)Find(form, "rtbChat")!;

            rtb.SelectionStart = Transcript(form).IndexOf("notes.txt", StringComparison.Ordinal);
            rtb.SelectionLength = 0;

            Cursor.Position = new Point(-32000, -32000);

            var menu = rtb.ContextMenuStrip!;
            menu.Show(rtb, new Point(8, 8));

            for (var i = 0; i < 12; i++)
            {
                Application.DoEvents();
                Thread.Sleep(25);
            }

            Assert.True(menu.Visible, "the caret is on a file that arrived, and that is enough");
            Assert.Equal("Save file to…", menu.Items[0].Text);

            menu.Close();
        });
    }

    // ---------------------------------------------------------------- the mouse
    //
    // These four drive a right click the way a mouse does - WM_CONTEXTMENU to the window, which is what makes
    // WinForms raise the menu attached to the control. Nothing else in the suite goes down that path, and that
    // is the whole reason "the tests are green" and "no menu appears" were able to be true at the same time:
    // CanSaveSharedAt answers the rule, and says nothing about whether a menu is attached to anything, wired up,
    // or cancelled on the way out.

    [Fact]
    public void ARightClickOnASharedPictureShowsTheSaveMenu()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Sharing(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", OnePixelPng));

            var menu = RightClickOn(form, "sunset.png");

            Assert.True(menu.Visible, "right-clicking a photo in the transcript puts a menu up");
            Assert.Equal("Save picture to…", menu.Items[0].Text);
            Assert.True(menu.Items[0].Enabled);

            menu.Close();
        });
    }

    [Fact]
    public void ARightClickOnAFileThatArrivedOffersToSaveIt()
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

            var job = service.SimulateIncomingFile("notes.txt", 4_096);
            service.AdvanceFile(job, 4_096);

            var menu = RightClickOn(form, "notes.txt");

            Assert.True(menu.Visible, "a file that came in can be saved from where it is written");
            Assert.Equal("Save file to…", menu.Items[0].Text);

            menu.Close();
        });
    }

    [Fact]
    public void ARightClickOnAnOrdinarySentenceShowsNoMenuAtAll()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Sharing(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;
            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "hello there"));

            var menu = RightClickOn(form, "hello there");

            // Not a greyed item: nothing. The window knows exactly what was right-clicked.
            Assert.False(menu.Visible);

            // But the bar says what the click was missing, because "right-click the file" doing nothing is
            // indistinguishable from a program that cannot save files - and was reported as one. Where to point is
            // not something anybody can be expected to guess.
            Assert.Contains("right-click the page", form.StatusText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ARightClickBelowTheLastEntryBelongsToNobody()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Sharing(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", OnePixelPng));

            var rtb = (RichTextBox)Find(form, "rtbChat")!;

            // The line break that ends the photo's own entry still counts as the photo - a click a hair past a
            // caption is a click on that line, and a menu that refuses across two pixels is read as an inability.
            Assert.True(form.CanSaveSharedAt(rtb.TextLength - 1));

            // Below everything, where the empty part of the window is, nothing is offered: no entry there, and a
            // menu that guesses which photo was meant is a photo opened that nobody clicked.
            Assert.False(form.CanSaveSharedAt(rtb.TextLength));
        });
    }

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
    private const int WM_CONTEXTMENU = 0x007B;

    [DllImport("user32.dll")]
    private static extern IntPtr PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Right-clicks the transcript where the given text is, and hands back the menu that belongs to it.
    /// </summary>
    private static ContextMenuStrip RightClickOn(Control form, string text)
    {
        var rtb = (RichTextBox)Find(form, "rtbChat")!;
        var index = Transcript(form).IndexOf(text, StringComparison.Ordinal);
        Assert.True(index >= 0, $"there is no {text} in the transcript to click on");

        var onScreen = rtb.PointToScreen(rtb.GetPositionFromCharIndex(index));

        // The menu asks where the mouse is, so the mouse has to be there - the same question the running program
        // answers, answered with the same information.
        Cursor.Position = onScreen;

        PostMessage(rtb.Handle, WM_CONTEXTMENU, rtb.Handle, (onScreen.Y << 16) | (onScreen.X & 0xFFFF));

        for (var i = 0; i < 12; i++)
        {
            Application.DoEvents();
            Thread.Sleep(25);
        }

        return rtb.ContextMenuStrip ?? throw new InvalidOperationException("the transcript has no menu attached");
    }

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
