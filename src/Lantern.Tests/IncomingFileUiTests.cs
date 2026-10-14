using System.Runtime.InteropServices;
using Lantern.Core;
using Lantern.Domain;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T5.4: the receiving half of F8. A file arrives whether or not anybody answers it - the bytes are already
/// on the way - so the window has to say so without stopping the person from typing, let them choose a
/// folder rather than a file name, show how far the file has got, and only put anything on disk once they
/// have said where and the last of it has turned up.
/// </summary>
public sealed class IncomingFileUiTests
{
    private const uint WmLButtonDown = 0x0201;

    private const uint WmLButtonUp = 0x0202;

    private const int MkLButton = 0x0001;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT pt);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;

        public int Y;
    }

    [Fact]
    public void AnIncomingFileIsOfferedWithoutTakingOverTheWindow()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new IncomingForm(service);

            form.Show();
            ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;

            Assert.Empty(form.FileOfferText);

            var job = service.SimulateIncomingFile("notes.txt", 4096);

            // Name, size and sender, which is what scenario 6 asks for.
            Assert.Contains("notes.txt", form.FileOfferText, StringComparison.Ordinal);
            Assert.Contains("User 2", form.FileOfferText, StringComparison.Ordinal);
            Assert.Contains("4", form.FileOfferText, StringComparison.Ordinal);

            // In the window rather than in a dialog: the room is still there to be typed in while the
            // person decides, which a modal box would not allow.
            Assert.True(((RichTextBox)Find(form, "txtMessage")!).Enabled);
            Assert.True(((Button)Find(form, "btnSend")!).Enabled);

            // The transcript says a file is coming from the moment it is announced - the sender's window
            // wrote its line at the same instant, and the room should read the same in both.
            Assert.Contains("[file] notes.txt", ((RichTextBox)Find(form, "rtbChat")!).Text, StringComparison.Ordinal);

            // Nothing has been written anywhere yet, and nothing will be until a folder is chosen.
            Assert.Empty(service.SaveRequests);
            Assert.Equal(1, form.FilesInFlight);
            Assert.False(job.IsComplete);

            form.Dispose();
        });
    }

    [Fact]
    public void TurningAFileDownThrowsAwayWhatHasArrived()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new IncomingForm(service);

            form.Show();

            var job = service.SimulateIncomingFile("big.iso", 4_000_000);
            service.AdvanceFile(job, 100_000);

            Click(form, "btnFileDecline");

            // The job is cancelled, which is what tells the transfer layer to delete the spool and to tell
            // whoever was sending it (F8: a decline that only changes this window's screen leaves the sender
            // pushing bytes into a machine that has already said no).
            Assert.True(job.Cancel.IsCancellationRequested);
            Assert.Empty(form.FileOfferText);
            Assert.Contains("turned down big.iso", form.StatusText, StringComparison.Ordinal);
            Assert.Empty(service.SaveRequests);

            // And it stopped counting: a bar sitting at 2% of a file nobody wants reads as "still going".
            service.AdvanceFile(job, 1_000_000);
            Assert.Equal(0, form.FilesInFlight);
            Assert.Equal(-1, form.FileProgressPercent);

            form.Dispose();
        });
    }

    /// <summary>
    /// The two buttons of the offer strip, reached the way a person reaches them: a mouse message put down at
    /// the pixel the strip claims they are on.
    /// </summary>
    /// <remarks>
    /// Everything else in this file answers a strip with <see cref="Button.PerformClick"/>, which tells the
    /// button to do its job and reports back. That is the right amount of politeness for testing what a button
    /// <i>does</i>, and it is no test at all of whether a pointer can <i>reach</i> it - and this project has
    /// been bitten by that difference before (v1.7: the right third of every photograph in the transcript was
    /// dead, because the character Windows reports under a click past the middle of a picture is the character
    /// after it, and every test in that file was clicking programmatically). The strip is a good candidate for
    /// the same kind of hole: it spends its life invisible, and then appears across the top of a window that
    /// was already full, in a panel that docks itself and reflows its own buttons.
    /// </remarks>
    [Fact]
    public void AMouseClickOnTheOfferStripReachesItsTwoButtons()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new IncomingForm(service);

            form.Show();

            // The pixel check below asks Windows who owns a point on the screen, which is only meaningful if this
            // window is the one on top at that moment.
            form.TopMost = true;
            form.BringToFront();
            form.Activate();

            // Anywhere plausible. The imaginary service only records where the person said; nothing is written
            // here, because the file is never completed in this test.
            form.Folder = Path.Combine(Path.GetTempPath(), "lantern-offer-strip-" + Guid.NewGuid().ToString("N"));

            var offered = service.SimulateIncomingFile("notes.txt", 4096);

            var save = (Button)Find(form, "btnFileSave");
            var decline = (Button)Find(form, "btnFileDecline");

            Assert.True(save.Visible, "a file was offered and the strip came up without a Save to… button");
            Assert.True(save.Enabled, "the file cannot be answered: Save to… is greyed out");
            Assert.True(decline.Visible, "a file was offered and the strip came up without a Decline button");
            Assert.True(decline.Enabled, "the file cannot be refused: Decline is greyed out");

            ClickWithMouse(form, save);

            // The folder chosen is the answer, and the file keeps its own name inside it.
            var request = Assert.Single(service.SaveRequests);
            Assert.Equal(offered, request.Job);
            Assert.Equal(Path.Combine(form.Folder, "notes.txt"), request.Path);

            // Answered, so no longer offered.
            Assert.Empty(form.FileOfferText);

            var second = service.SimulateIncomingFile("big.iso", 4_000_000);

            Assert.Contains("big.iso", form.FileOfferText, StringComparison.Ordinal);

            ClickWithMouse(form, decline);

            // The strip going away is not the decline. The bytes have to be stopped, or the person who was
            // sending them goes on sending them to somebody who has already said no.
            Assert.True(second.Cancel.IsCancellationRequested, "the strip came down but nobody told the sender");
            Assert.Empty(form.FileOfferText);
            Assert.Contains("turned down big.iso", form.StatusText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public Task AChosenFolderGetsTheFileWhenAllOfItHasArrived()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new IncomingForm(service);
            var folder = NewFolder();

            form.Show();
            form.Folder = folder;

            var job = service.SimulateIncomingFile("notes.txt", 4096);

            Click(form, "btnFileSave");

            // The folder is the choice, and the file keeps its own name inside it - which is the only thing
            // that works when three files arrive in the same minute.
            var request = Assert.Single(service.SaveRequests);
            Assert.Equal(job, request.Job);
            Assert.Equal(Path.Combine(folder, "notes.txt"), request.Path);

            // Not yet written: it was asked for a place to put it, not told the file had finished.
            Assert.False(File.Exists(request.Path));

            service.AdvanceFile(job, 4096);

            await Watch.UntilAsync(() => File.Exists(request.Path), "the file to be written once it had all arrived");
            Assert.Equal("simulated payload for notes.txt", await ReadAfterAnyoneElseHasLetGoAsync(request.Path));

            try
            {
                // Patient on purpose. The write itself is waited for above, so by here the only thing outstanding
                // is the window being told, which is a message on a queue - and on a machine running four hundred
                // tests at once a queue can take longer than eight seconds. Every other run of this test says the
                // line, so the remedy is patience plus evidence, not a shorter fuse.
                await Watch.UntilAsync(
                    () => form.StatusText.Contains("saved notes.txt", StringComparison.Ordinal),
                    "the window to say where it put the file",
                    Watch.DefaultTimeoutMs * 3);
            }
            catch (TimeoutException)
            {
                // What the bar did say is the whole of the evidence about a failure that happens only in a full
                // parallel run and nowhere else, so the failure has to carry it out of the test.
                throw new Xunit.Sdk.XunitException(
                    $"the window never said it had saved the file. The bar says: \"{form.StatusText}\"");
            }

            Assert.Contains(folder, form.StatusText, StringComparison.Ordinal);

            // The offer strip is down: this file has been answered, and the next one to arrive will be
            // written into the folder already chosen.
            Assert.Empty(form.FileOfferText);

            form.Dispose();
        });

    [Fact]
    public void NoFolderChosenKeepsTheOfferWhereItWas()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new IncomingForm(service);

            form.Show();
            form.Folder = null;

            var job = service.SimulateIncomingFile("notes.txt", 4096);

            Click(form, "btnFileSave");

            // Closing the folder box is not a refusal. The offer stays, the bytes keep coming, and the person
            // gets to decide properly instead of having lost a file they meant to keep.
            Assert.Contains("notes.txt", form.FileOfferText, StringComparison.Ordinal);
            Assert.False(job.Cancel.IsCancellationRequested);
            Assert.Empty(service.SaveRequests);

            form.Dispose();
        });
    }

    [Fact]
    public Task AFileThatArrivedWhileNobodyWasAnsweringIsStillOffered()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new IncomingForm(service);

            form.Show();

            var job = service.SimulateIncomingFile("notes.txt", 4096);
            service.AdvanceFile(job, 4096);

            // The transfer finished while nobody had said where it should go. The bytes are in Lantern's own
            // area and will wait there, so the offer has to still be on the screen when the person looks back
            // at the window - otherwise a file that arrived while they were typing is a file that vanished.
            await Watch.UntilAsync(() => job.IsComplete, "the file to arrive");
            await Task.Delay(200);

            Assert.Contains("notes.txt", form.FileOfferText, StringComparison.Ordinal);
            Assert.False(job.Cancel.IsCancellationRequested);
            Assert.Empty(service.SaveRequests);

            // And it can still be had: the offer is not a moment that passed.
            form.Folder = NewFolder();
            Click(form, "btnFileSave");

            await Watch.UntilAsync(() => service.SaveRequests.Count == 1, "the late answer to be accepted");
            Assert.True(File.Exists(Path.Combine(form.Folder!, "notes.txt")));

            form.Dispose();
        });

    [Fact]
    public Task AFolderThatWontHaveItIsAskedAgainRatherThanLost()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new IncomingForm(service);

            // A folder that will not take the file, which is what a read-only share looks like from here: a
            // name Windows refuses to create because a folder of that name is standing in the way.
            var stubborn = NewFolder();
            Directory.CreateDirectory(Path.Combine(stubborn, "notes.txt"));

            var sensible = NewFolder();

            form.Show();
            form.Folder = stubborn;

            var job = service.SimulateIncomingFile("notes.txt", 4096);
            service.AdvanceFile(job, 4096);

            Click(form, "btnFileSave");

            await Watch.UntilAsync(
                () => form.FileOfferText.Contains("notes.txt", StringComparison.Ordinal),
                "the offer to come back after the folder refused it");

            // The bytes are still in Lantern's own area, so this is a folder that would not have the file
            // rather than a file that has gone - and being asked again is the whole recovery. Without it,
            // "that folder is read-only" would mean "send it again".
            Assert.Empty(form.FileProgressText);
            Assert.False(File.Exists(Path.Combine(stubborn, "notes.txt")));
            Assert.False(job.Cancel.IsCancellationRequested);

            form.Folder = sensible;
            Click(form, "btnFileSave");

            var saved = Path.Combine(sensible, "notes.txt");
            await Watch.UntilAsync(() => File.Exists(saved), "the same file to land in a folder that would take it");
            Assert.Equal("simulated payload for notes.txt", await File.ReadAllTextAsync(saved));
            Assert.Equal(2, service.SaveRequests.Count);

            form.Dispose();
        });

    [Fact]
    public Task TheBarShowsHowMuchOfAFileHasArrived()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new IncomingForm(service);

            form.Show();
            form.Folder = NewFolder();

            var job = service.SimulateIncomingFile("notes.txt", 1000);

            // The transfer is moving, so there is something to stop, even before anybody has answered it.
            Click(form, "btnFileSave");

            Assert.True(form.CanStopFile);
            Assert.Contains("notes.txt", form.FileProgressText, StringComparison.Ordinal);
            Assert.Equal(0, form.FileProgressPercent);

            service.AdvanceFile(job, 500);

            // The bar is read from the job rather than driven by it, so it catches up on the next poll
            // rather than on the byte.
            await Watch.UntilAsync(() => form.FileProgressPercent == 50, "the bar to show half the file");
            Assert.Contains("50", form.FileProgressText, StringComparison.Ordinal);

            service.AdvanceFile(job, 500);

            await Watch.UntilAsync(() => form.FileProgressPercent == -1, "the bar to go when the file is over");
            Assert.False(form.CanStopFile);
            Assert.Equal(0, form.FilesInFlight);

            form.Dispose();
        });

    [Fact]
    public Task StoppingAFileTakesTheBarAway()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new IncomingForm(service);

            form.Show();
            form.Folder = NewFolder();

            var job = service.SimulateIncomingFile("notes.txt", 1000);
            Click(form, "btnFileSave");
            service.AdvanceFile(job, 200);

            await Watch.UntilAsync(() => form.FileProgressPercent == 20, "the bar to catch up");

            form.StopCurrentFile();

            Assert.True(job.Cancel.IsCancellationRequested);
            Assert.Contains("stopped notes.txt", form.StatusText, StringComparison.Ordinal);

            await Watch.UntilAsync(() => form.FileProgressPercent == -1, "the bar to go");
            Assert.False(form.CanStopFile);

            form.Dispose();
        });

    [Fact]
    public void TwoFilesArrivingAtOnceAreOfferedOneAfterTheOther()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new IncomingForm(service);

            form.Show();

            var first = service.SimulateIncomingFile("notes.txt", 4096);
            var second = service.SimulateIncomingFile("photo.zip", 8192, from: "Nadia");

            // Only one strip, saying another is waiting: two dialogs stacked on each other would only show
            // the top one anyway, and the second offer would be invisible behind the first.
            Assert.Contains("notes.txt", form.FileOfferText, StringComparison.Ordinal);
            Assert.Contains("1 more waiting", form.FileOfferText, StringComparison.Ordinal);

            // Two files moving, one bar - and the bar says there is more than one, so it is not read as the
            // progress of the file being looked at.
            Assert.Equal(2, form.FilesInFlight);
            Assert.Contains("(+1)", form.FileProgressText, StringComparison.Ordinal);

            Click(form, "btnFileDecline");

            Assert.True(first.Cancel.IsCancellationRequested);
            Assert.Contains("photo.zip", form.FileOfferText, StringComparison.Ordinal);
            Assert.Contains("Nadia", form.FileOfferText, StringComparison.Ordinal);
            Assert.DoesNotContain("waiting", form.FileOfferText, StringComparison.Ordinal);

            // The one that was turned down has stopped counting, in the strip and on the bar both.
            Assert.Equal(1, form.FilesInFlight);
            Assert.DoesNotContain("(+1)", form.FileProgressText, StringComparison.Ordinal);

            form.Dispose();
        });
    }

    [Fact]
    public Task AFileStoppedHalfwaySaysSoAndSavesNothing()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new IncomingForm(service);
            var folder = NewFolder();

            form.Show();
            form.Folder = folder;

            var job = service.SimulateIncomingFile("notes.txt", 4096);
            Click(form, "btnFileSave");

            service.AdvanceFile(job, 1000);
            service.SimulateFileStopped(job);

            await Watch.UntilAsync(() => form.FileProgressPercent == -1, "the bar to give up on it");

            // Asked for and never written: a half file in the folder, with the name of the real one on it,
            // is worse than no file.
            Assert.False(File.Exists(Path.Combine(folder, "notes.txt")));
            Assert.Single(service.SaveRequests);
            Assert.Contains("did not finish", form.StatusText, StringComparison.Ordinal);

            form.Dispose();
        });

    [Fact]
    public Task TheOfferComesDownWhenTheSenderGivesUp()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new IncomingForm(service);

            form.Show();

            var job = service.SimulateIncomingFile("notes.txt", 4096);
            Assert.NotEmpty(form.FileOfferText);

            service.SimulateFileStopped(job);

            // A strip offering a file that no longer exists would be a button to nothing: Decline there
            // would cancel a transfer that is already over.
            await Watch.UntilAsync(() => form.FileOfferText.Length == 0, "the offer to come down");
            Assert.False(job.IsComplete);
            Assert.Empty(service.SaveRequests);

            form.Dispose();
        });

    [Fact]
    public Task AFileSentFromThisWindowUsesTheSameBar()
        => Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao")
            {
                FilesCompleteImmediately = false,
            };

            using var form = new IncomingForm(service);
            var file = Write("deck.pdf", 2000);

            try
            {
                form.Show();
                ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;
                form.Answer = file;

                Click(form, "btnShare");

                // One bar for both directions, because the question a person asks of it - "how far has it
                // got?" - is the same question whichever way the file is going.
                await Watch.UntilAsync(() => form.FileProgressPercent == 0, "the bar to appear for the outgoing file");
                Assert.Contains("deck.pdf", form.FileProgressText, StringComparison.Ordinal);
                Assert.True(form.CanStopFile);

                form.StopCurrentFile();

                await Watch.UntilAsync(() => form.FileProgressPercent == -1, "the bar to go when it is stopped");
            }
            finally
            {
                Delete(file);
                form.Dispose();
            }
        });

    // -------------------------------------------------------------------------- harness

    /// <summary>A window with a person's folder choice answered from outside instead of by a dialog.</summary>
    private sealed class IncomingForm : Lantern.UI.HostForm
    {
        public IncomingForm(IChatService service) : base(service)
        {
        }

        /// <summary>Where this imaginary person says to put it; null closes the box without choosing.</summary>
        public string? Folder;

        /// <summary>Their choice of something to share (T5.1's seam, reused for the outgoing bar).</summary>
        public string? Answer;

        protected override string? AskForFolder(string fileName) => Folder;

        protected override string? AskForAttachment() => Answer;
    }

    private static void Click(Control form, string name)
    {
        var control = Find(form, name);

        // Show() first, or the click does nothing: a button that has never been given a handle does not
        // think it is on a window.
        ((Button)control).PerformClick();
    }

    /// <summary>
    /// Left-clicks the middle of a control with mouse messages, as a pointer does, rather than asking the
    /// control to click itself.
    /// </summary>
    /// <remarks>
    /// Two things are checked, because a click that works has two parts: who is actually at that pixel, and what
    /// happens when the message gets there. Windows does not pass a mouse message from a parent to a child - the
    /// message pump asks who owns the pixel and addresses the message to that window, in that window's own
    /// coordinates - so the messages below go to the button's handle, at the middle of it.
    /// </remarks>
    private static void ClickWithMouse(Control form, Control button)
    {
        var box = button.RectangleToScreen(button.ClientRectangle);
        var onScreen = new Point(box.Left + box.Width / 2, box.Top + box.Height / 2);

        // Who Windows says owns that pixel. This is the one part of the test that needs the window to actually be
        // the window the person is looking at, so the form is put on top for the duration - and it is the part
        // worth the trouble, because a button the message pump never addresses is a button whose handler is
        // never asked, however correct that handler is.
        Assert.Equal(button.Handle, WindowFromPoint(new POINT { X = onScreen.X, Y = onScreen.Y }));

        var lParam = (IntPtr)(((box.Height / 2) << 16) | (ushort)(box.Width / 2));

        SendMessage(button.Handle, WmLButtonDown, (IntPtr)MkLButton, lParam);
        SendMessage(button.Handle, WmLButtonUp, IntPtr.Zero, lParam);

        for (var i = 0; i < 20; i++)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static Control Find(Control parent, string name)
        => parent.Controls.Find(name, searchAllChildren: true).FirstOrDefault()
            ?? throw new InvalidOperationException($"no control named {name}");

    /// <summary>
    /// Reading a file that has just appeared on Windows means asking again. Something inspects a new file the
    /// moment it lands - antivirus, the search indexer, a thumbnailer - and holds it in a way that refuses a
    /// second reader for a moment. The program is done with it by then: the save is a rename out of the spool,
    /// and a rename cannot happen while the source is open. So this waits for whoever else was looking, and
    /// still fails on the bytes if the bytes are wrong.
    /// </summary>
    private static async Task<string> ReadAfterAnyoneElseHasLetGoAsync(string path)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(Watch.DefaultTimeoutMs * 2);

        while (true)
        {
            try
            {
                return await File.ReadAllTextAsync(path);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
        }
    }

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lantern-t54-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Write(string name, int size)
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[size]);
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
            // Leftover files in the temp folder are untidy, not a failure.
        }
    }
}
