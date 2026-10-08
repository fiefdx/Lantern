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
            Assert.True(((TextBox)Find(form, "txtMessage")!).Enabled);
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
            Assert.Equal("simulated payload for notes.txt", await File.ReadAllTextAsync(request.Path));

            await Watch.UntilAsync(
                () => form.StatusText.Contains("saved notes.txt", StringComparison.Ordinal),
                "the window to say where it put the file");

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

    private static Control Find(Control parent, string name)
        => parent.Controls.Find(name, searchAllChildren: true).FirstOrDefault()
            ?? throw new InvalidOperationException($"no control named {name}");

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
