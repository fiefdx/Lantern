using Lantern.Core;
using Lantern.Domain;
using Lantern.Storage;

namespace Lantern.UI;

/// <summary>
/// The window both roles share (Figures 3 and 4): rooms on the left, transcript in the middle,
/// members on the right, message box along the bottom.
/// <para>
/// Nothing here knows where the data comes from: every room, member and message is read from
/// <see cref="Service"/> and every change is pushed back through it. That is what lets the same
/// window run on the in-memory <see cref="FakeChatService"/> today and on the real chat engine
/// later — the UI never talks to the transport.
/// </para>
/// Roles differ only in which buttons the subclass drops into <see cref="pnlRoomActions"/>.
/// </summary>
public abstract partial class ChatForm : Form
{
    /// <summary>True while a list is being rebuilt, so the rebuild does not re-enter itself.</summary>
    private bool syncing;

    private bool busy;

    /// <summary>
    /// Which picture, if any, is drawn on each transcript line (F7). Keyed by the line as it was
    /// written, because that is what the transcript can be asked about: the double click knows the text
    /// under the caret and nothing else. Rebuilt whenever the transcript is redrawn.
    /// </summary>
    private readonly Dictionary<string, PictureMessage> openable = new(StringComparer.Ordinal);

    protected ChatForm(IChatService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        Service = service;

        InitializeComponent();

        txtMessage.MaxLength = ValidationRules.MessageMaxLength;
        lblStatus.Text = service.IsHost ? "Status: hosting, waiting for clients" : "Status: connected to server";

        BindService();
        ShowRooms();
        ShowMembers();
        ShowTranscript(); // no room entered yet: Send, Share and the box all start disabled
    }

    /// <summary>Everything this window knows about rooms, members and traffic.</summary>
    protected IChatService Service { get; }

    /// <summary>The room this window is in, or <see langword="null"/> when none is selected.</summary>
    protected Room? CurrentRoom { get; private set; }

    /// <summary>Name of the highlighted room in the list, whether or not it has been entered.</summary>
    protected string? SelectedRoom => lstRooms.SelectedItem as string;

    // ------------------------------------------------------------- plumbing

    private void BindService()
    {
        Service.MessageReceived += (_, message) => OnUi(() => ShowIncoming(message));
        Service.RoomsChanged += (_, _) => OnUi(ShowRooms);
        Service.MembersChanged += (_, _) => OnUi(ShowMembers);
        Service.StatusChanged += (_, status) => OnUi(() => SetStatus(status));
        Service.IncomingFile += (_, job) => OnUi(() => OfferFile(job));
        Service.ConnectionLost += (_, error) => OnUi(() => OnConnectionLost(error));
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread, dropping it if the window is closing.</summary>
    private void OnUi(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        try
        {
            // Before the window has a handle there is nothing to marshal on to: the form was built
            // on this very thread, so run it here.
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {
            // The window went away while the event was in flight.
        }
    }

    /// <summary>A service call refuses something the user asked for (duplicate name, over-long message).</summary>
    protected void SetStatus(string message)
        => lblStatus.Text = message.Length == 0 ? "Status: idle" : $"Status: {message}";

    /// <summary>
    /// What the status bar says. Readable from outside because it is the only place this program explains
    /// itself — and because a window that has stepped aside on its own no longer has a control tree to
    /// look through.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string StatusText => lblStatus.Text ?? string.Empty;

    /// <summary>
    /// Runs a service call with Send and Share greyed out, and reports a refusal in the status bar
    /// instead of letting it reach the message loop.
    /// </summary>
    protected async Task RunAsync(Func<Task> call)
    {
        SetBusy(true);
        try
        {
            await call();
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus($"failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Grey out Send and Share while an operation is in flight.</summary>
    protected bool IsBusy => busy;

    private void SetBusy(bool value)
    {
        busy = value;
        btnSend.Enabled = !value && CurrentRoom is not null;
        btnShare.Enabled = !value && CurrentRoom is not null;
        UseWaitCursor = value;
    }

    // ----------------------------------------------------------- room list

    /// <summary>Rebuilds the room list from the service, keeping whatever is selected.</summary>
    private void ShowRooms()
    {
        syncing = true;
        var selected = SelectedRoom;
        lstRooms.BeginUpdate();
        lstRooms.Items.Clear();
        foreach (var room in Service.Rooms)
        {
            lstRooms.Items.Add(room.Name);
        }

        lstRooms.EndUpdate();
        syncing = false;

        var index = lstRooms.Items.IndexOf((object?)selected ?? string.Empty);
        if (index >= 0)
        {
            lstRooms.SelectedIndex = index;
        }
        else if (CurrentRoom is not null)
        {
            // The room we were in disappeared (the host deleted it).
            LeaveRoom(announce: false);
        }
    }

    private void LstRooms_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!syncing && lstRooms.SelectedItem is string name && name != CurrentRoom?.Name)
        {
            _ = EnterRoomAsync(name);
        }
    }

    private async Task EnterRoomAsync(string name)
    {
        await RunAsync(async () =>
        {
            await Service.JoinRoomAsync(name);
            CurrentRoom = Service.Rooms.FirstOrDefault(r => r.Name == name);
            ShowMembers();
            ShowTranscript();
            SetStatus(CurrentRoom is null ? $"failed: no room \"{name}\"" : $"in room \"{name}\"");
        });
    }

    /// <summary>Redraws the transcript from the current room's own history (F6).</summary>
    private void ShowTranscript()
    {
        rtbChat.Clear();
        openable.Clear();
        var editable = CurrentRoom is not null;
        txtMessage.Enabled = editable;
        if (!busy)
        {
            btnSend.Enabled = editable;
            btnShare.Enabled = editable;
        }

        if (CurrentRoom is null)
        {
            lblMembersHeader.Text = "Members";
            Append(new SystemMessage(string.Empty, $"Welcome, {Service.Nickname}! Select a room to enter."));
            return;
        }

        lblMembersHeader.Text = $"Members ({CurrentRoom.Members.Count})";
        foreach (var message in CurrentRoom.Transcript)
        {
            Append(message);
        }
    }

    /// <summary>Drops out of the current room; <paramref name="announce"/> leaves a notice behind.</summary>
    protected void LeaveRoom(bool announce)
    {
        if (announce && CurrentRoom is { } room)
        {
            room.Append(SystemMessage.Leave(room.Name, Service.Nickname));
        }

        CurrentRoom = null;
        ShowTranscript();
        ShowMembers();
    }

    // ---------------------------------------------------------- member list

    private void ShowMembers()
    {
        lstMembers.BeginUpdate();
        lstMembers.Items.Clear();
        foreach (var user in Service.Members)
        {
            lstMembers.Items.Add(user.Nickname == Service.Nickname ? $"{user.Nickname} (you)" : user.Nickname);
        }

        lstMembers.EndUpdate();
        if (CurrentRoom is not null)
        {
            lblMembersHeader.Text = $"Members ({Service.Members.Count})";
        }
    }

    // --------------------------------------------------------------- sending

    private void BtnSend_Click(object? sender, EventArgs e) => _ = SendMessageAsync();

    private void TxtMessage_KeyDown(object? sender, KeyEventArgs e)
    {
        // Enter sends, Shift+Enter adds a line break (scenario 4).
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            _ = SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        var body = txtMessage.Text.Trim();
        if (CurrentRoom is not { } room || body.Length == 0)
        {
            return;
        }

        if (ValidationRules.MessageError(body) is { } error)
        {
            SetStatus(error);
            return;
        }

        var message = new TextMessage(Service.Nickname, room.Name, body);
        await RunAsync(async () =>
        {
            await Service.SendAsync(message);
            txtMessage.Clear();
            Append(message);
        });
    }

    private void BtnShare_Click(object? sender, EventArgs e) => _ = ShareAsync();

    /// <summary>
    /// F7 and F8: one Share button for pictures and files alike, because the person clicking has a thing
    /// they want to send and should not be asked a second question about what kind of thing it is. The
    /// name decides (see <see cref="Attachments"/>), and a name nobody recognises becomes a file, which is
    /// the safe answer: a file arrives complete and they open it themselves.
    /// </summary>
    private async Task ShareAsync()
    {
        if (CurrentRoom is not { } room)
        {
            SetStatus("select a room first");
            return;
        }

        var path = AskForAttachment();

        if (path is null)
        {
            // Changed their mind, which is not a failure and needs nothing said about it.
            return;
        }

        var file = new FileInfo(path);

        if (!file.Exists)
        {
            SetStatus("that file could not be read");
            return;
        }

        if (Attachments.IsPicture(path))
        {
            await SharePictureAsync(room, file);
        }
        else
        {
            ShareFile(room, file);
        }
    }

    /// <summary>
    /// F7: a picture's bytes are the message, so they are read here and now - a photo is at most a few
    /// megabytes, and it is already sitting in memory on the way to the transcript.
    /// </summary>
    private async Task SharePictureAsync(Room room, FileInfo file)
    {
        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Somebody else has it open, or the drive went away between the dialog and this line. The
            // window is still a window.
            SetStatus($"could not read {file.Name}: {ex.Message}");
            return;
        }

        // Said here, while the person is still looking at the folder they picked from. The host would
        // refuse it too - and does, for the peer that does not check - but a 40 MB photo should not have
        // to travel to the host and back to be turned down.
        if (ValidationRules.PictureError(file.Name, bytes.LongLength) is { } error)
        {
            SetStatus(error);
            return;
        }

        var picture = new PictureMessage(Service.Nickname, room.Name, file.Name, bytes);

        await RunAsync(async () =>
        {
            await Service.SendAsync(picture);
            Append(picture);
            SetStatus($"sent {file.Name}");
        });
    }

    /// <summary>
    /// F8: the bytes are not read here. A file is announced and then carried in numbered chunks, which is
    /// what makes it work for a file bigger than the memory of the machine sending it - and it is why this
    /// returns at once instead of waiting: the job that comes back is the thing to watch, not this call.
    /// </summary>
    private void ShareFile(Room room, FileInfo file)
    {
        FileTransferJob job;

        try
        {
            job = Service.SendFile(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The file went away, or is too big, or is being held by something else. All of them are
            // worth one line in the status bar and a window that is still a window.
            SetStatus(ex.Message);
            return;
        }

        // Built from the job rather than from what this window knows, so the id in this transcript is the
        // id every other window's transcript has for the same file (F8: one event, seen four times).
        Append(new FileMessage(job.SenderNickname, job.RoomName, job.FileName, job.SizeBytes, job.Id, id: job.MessageId));

        WatchSend(job);
        SetStatus($"sending {file.Name}");
    }

    /// <summary>
    /// Puts a sending file's progress where a person can see it, and stops the transfer if they ask.
    /// </summary>
    private void WatchSend(FileTransferJob job)
    {
        Track(job);
        ShowFileProgress();
    }

    /// <summary>
    /// Asks for something to send; null means nothing was chosen. One method to override, so the decision
    /// made from the choice can be tested without a dialog standing in front of the test runner.
    /// </summary>
    protected virtual string? AskForAttachment()
    {
        using var picker = new OpenFileDialog
        {
            Title = "Share",
            Filter = $@"{Attachments.PictureFilter}|All files (*.*)|*.*",
        };

        return picker.ShowDialog(this) == DialogResult.OK ? picker.FileName : null;
    }

    // ------------------------------------------------------------- receiving
    //
    // F8, the other half: a file arrives whether or not anybody answers, because the bytes are already on
    // their way. So the window says so in a strip above the input rather than in a dialog - the person can
    // keep typing, and a second offer has somewhere to wait - and the file goes to disk only when they say
    // where. Turning one down throws the bytes away and tells the sender, which is the transfer layer's job
    // rather than this window's.

    /// <summary>Files offered to this window that have not been answered yet, oldest first.</summary>
    private readonly List<FileTransferJob> unanswered = [];

    /// <summary>Files this window has agreed to keep and is waiting to write out.</summary>
    private readonly List<FileTransferJob> accepted = [];

    /// <summary>Every file in flight either way, which between them own the progress bar.</summary>
    private readonly List<FileTransferJob> inFlight = [];

    /// <summary>The offer the notice strip is about, or null when the strip is down.</summary>
    private FileTransferJob? offered;

    /// <summary>The file the bar, its label and the Stop button belong to.</summary>
    private FileTransferJob? shown;

    /// <summary>Files this window stopped itself - their ending needs no comment on top of the one given.</summary>
    private readonly HashSet<FileTransferJob> stoppedHere = [];

    /// <summary>Scenario 6: name, size and sender, then Save to… or Decline.</summary>
    private void OfferFile(FileTransferJob job)
    {
        // Said here, when the file is announced, rather than when it arrives: the sender's window wrote this
        // line the moment it sent, and a room whose transcript reads differently in different windows is a
        // room where two people cannot agree about what happened (F8: one event, seen in each window).
        Append(new FileMessage(job.SenderNickname, job.RoomName, job.FileName, job.SizeBytes, job.Id, id: job.MessageId));

        Track(job);
        unanswered.Add(job);
        ShowOffer();
    }

    /// <summary>
    /// Starts watching a transfer in either direction: it owns the progress bar from now until it is over,
    /// however it ends.
    /// </summary>
    private void Track(FileTransferJob job)
    {
        inFlight.Add(job);

        _ = job.Finished.ContinueWith(
            _ => OnUi(() => FileFinished(job)),
            TaskScheduler.Default);

        // Polled rather than driven by the job's progress callback. A chunk is 8 KiB, so a hundred megabyte
        // file is twelve thousand reports and a window that repaints four times a second is the same window
        // as this one either way - and reading BytesDone cannot miss one, cannot arrive on the wrong thread,
        // and cannot be the reason a transfer waits.
        timerProgress.Start();
        ShowFileProgress();
    }

    private void TimerProgress_Tick(object? sender, EventArgs e) => ShowFileProgress();

    /// <summary>Redraws the progress part of the status bar from whichever file is furthest ahead.</summary>
    private void ShowFileProgress()
    {
        shown = accepted.LastOrDefault(Moving)
            ?? inFlight.LastOrDefault(Moving);

        if (shown is null)
        {
            inFlight.RemoveAll(j => !Moving(j));
            accepted.RemoveAll(j => !Moving(j));

            progressFile.Visible = false;
            lblFileProgress.Visible = false;
            btnFileStop.Visible = false;
            timerProgress.Stop();

            return;
        }

        var others = inFlight.Count(j => Moving(j) && j != shown);

        progressFile.Value = (int)Math.Round(shown.Fraction * progressFile.Maximum);
        progressFile.Visible = true;

        lblFileProgress.Text = $"{shown.FileName} {shown.Fraction:P0}"
            + (accepted.Contains(shown) ? ", saving" : string.Empty)
            + (others > 0 ? $" (+{others})" : string.Empty);
        lblFileProgress.Visible = true;

        // Stopping is offered in both directions. Half of a file is worth stopping whether it was coming or
        // going, and the person who notices is the one holding the mouse.
        btnFileStop.Visible = true;
    }

    private void FileFinished(FileTransferJob job)
    {
        accepted.Remove(job);
        unanswered.Remove(job);
        stoppedHere.Remove(job);

        if (job.Cancel.IsCancellationRequested)
        {
            // Somebody stopped it - this window, the person at the other end, or a machine that could not
            // drain fast enough - and one of them has already said so in a line of its own. What this adds is
            // the bar going away: a number stuck at 34% reads as "still going" to anybody who glances at it.
            if (!stoppedHere.Contains(job) && shown == job)
            {
                SetStatus($"{job.FileName} did not finish");
            }

            // The offer goes down with it: a strip offering a file that no longer exists is a Decline button
            // pointed at nothing.
            ShowOffer();
        }
        else if (shown == job && !accepted.Contains(job))
        {
            // "arrived" rather than "saved": what has arrived is not yet anywhere permanent, and a window
            // that says saved before it has written would be believed.
            SetStatus($"{job.FileName} {(job.SenderNickname == Service.Nickname ? "sent" : "arrived")}");
        }

        ShowFileProgress();
    }

    /// <summary>Still going: not over, and not stopped. A stopped transfer counts as over everywhere.</summary>
    private static bool Moving(FileTransferJob job) => !job.Finished.IsCompleted && !job.Cancel.IsCancellationRequested;

    /// <summary>Puts the oldest unanswered offer in the strip, or takes the strip down.</summary>
    private void ShowOffer()
    {
        unanswered.RemoveAll(j => j.Cancel.IsCancellationRequested || j.Finished.IsCompleted);

        if (unanswered.Count == 0)
        {
            offered = null;
            pnlFileNotice.Visible = false;
            lblFileNotice.Text = string.Empty;

            return;
        }

        var job = unanswered[0];
        var waiting = unanswered.Count - 1;

        offered = job;
        lblFileNotice.Text = $"{job.SenderNickname} is sending {job.FileName} ({FileSize.Describe(job.SizeBytes)})"
            + (waiting > 0 ? $" - {waiting} more waiting" : string.Empty);
        pnlFileNotice.Visible = true;
    }

    private void BtnFileSave_Click(object? sender, EventArgs e)
    {
        if (offered is not { } job)
        {
            return;
        }

        var folder = AskForFolder(job.FileName);

        if (folder is null)
        {
            // No folder chosen is not a refusal - the strip stays up, the bytes keep arriving, and the person
            // can decide properly instead of having lost the file.
            return;
        }

        unanswered.Remove(job);
        accepted.Add(job);
        ShowOffer();
        ShowFileProgress();

        _ = SaveReceivedAsync(job, Path.Combine(folder, job.FileName));
    }

    /// <summary>
    /// Moves a completed transfer from the spool to where the person said. Returns when the file is
    /// written, which for a file still arriving means whenever the last of it turns up.
    /// </summary>
    private async Task SaveReceivedAsync(FileTransferJob job, string path)
    {
        try
        {
            await Service.SaveReceivedFileAsync(job, path).ConfigureAwait(false);
            OnUi(() => SetStatus($"saved {job.FileName} to {Path.GetDirectoryName(path)}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // The folder was read-only, or something with that name was already in it, or the file stopped
            // short on the way. All of them are one line in the status bar, and the spool is still there so
            // the person can pick a different folder and try again.
            OnUi(() =>
            {
                accepted.Remove(job);
                SetStatus(ex.Message);
            });
        }
        catch (OperationCanceledException)
        {
            // The transfer was stopped, which is said elsewhere and louder.
        }
    }

    private void BtnFileDecline_Click(object? sender, EventArgs e)
    {
        if (offered is not { } job)
        {
            return;
        }

        unanswered.Remove(job);
        ShowOffer();

        // Cancelling the job *is* the decline: the spool is thrown away and the sender is told, both of which
        // belong to the transfer rather than to this window.
        stoppedHere.Add(job);
        job.CancelTransfer();
        SetStatus($"turned down {job.FileName}");

        // Straight away rather than on the next tick: a bar left at 3% of a file that was just thrown away is
        // a bar telling a lie about a file nobody is receiving any more.
        ShowFileProgress();
    }

    private void BtnFileStop_Click(object? sender, EventArgs e) => StopCurrentFile();

    /// <summary>
    /// Stops the file the bar is about. Public for the same reason <see cref="OpenTranscriptLineAtCaret" />
    /// is: it is what a control does, written down where it can be called without a mouse.
    /// </summary>
    public void StopCurrentFile()
    {
        if (shown is { } job && Moving(job))
        {
            stoppedHere.Add(job);
            job.CancelTransfer();
            accepted.Remove(job);
            SetStatus($"stopped {job.FileName}");
            ShowFileProgress();
            ShowOffer();
        }
    }

    /// <summary>
    /// Asks for a folder to put an incoming file in; null means none was chosen. The file keeps its own
    /// name inside it - a receiver choosing a whole folder is saying where, not what, which is also the only
    /// thing that works when three files arrive in the same minute.
    /// </summary>
    protected virtual string? AskForFolder(string fileName)
    {
        using var picker = new FolderBrowserDialog
        {
            Description = $"Where should {fileName} go?",
            UseDescriptionForTitle = true,
        };

        return picker.ShowDialog(this) == DialogResult.OK ? picker.SelectedPath : null;
    }

    /// <summary>What the offer strip says, or empty when nothing is being offered.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string FileOfferText => pnlFileNotice.Visible ? lblFileNotice.Text ?? string.Empty : string.Empty;

    /// <summary>The percentage on the bar, or -1 when no file is in flight.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int FileProgressPercent => progressFile.Visible ? (int)Math.Round(progressFile.Value * 100.0 / progressFile.Maximum) : -1;

    /// <summary>What the bar is labelled with - which file, how far, and how many others are behind it.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string FileProgressText => lblFileProgress.Visible ? lblFileProgress.Text ?? string.Empty : string.Empty;

    /// <summary>True while the Stop button is offering to stop something.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool CanStopFile => btnFileStop.Visible;

    /// <summary>How many files this window has moving, in either direction.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int FilesInFlight => inFlight.Count(Moving);

    // ------------------------------------------------------------- F7: opening a picture

    /// <summary>
    /// F7: a thumbnail is 160 pixels; the photo is not. Double-clicking the caption opens the picture
    /// itself, because "share a photo" that nobody can actually look at is only half a feature.
    /// </summary>
    private void RtbChat_MouseDoubleClick(object? sender, MouseEventArgs e) => OpenTranscriptLineAtCaret();

    /// <summary>Opens whatever picture the transcript line under the caret carries, if any.</summary>
    public void OpenTranscriptLineAtCaret()
    {
        if (openable.Count == 0)
        {
            return;
        }

        var lineIndex = rtbChat.GetLineFromCharIndex(rtbChat.SelectionStart);

        if (lineIndex >= 0 && lineIndex < rtbChat.Lines.Length)
        {
            OpenTranscriptLine(rtbChat.Lines[lineIndex]);
        }
    }

    /// <summary>
    /// Opens the picture shown on a transcript line. Separate from the mouse so the rule - a line that
    /// is not a picture does nothing - is answerable without a caret, a click and a window.
    /// </summary>
    public void OpenTranscriptLine(string line)
    {
        if (openable.GetValueOrDefault(line) is { } picture)
        {
            OpenPicture(picture);
        }
    }

    /// <summary>
    /// Puts the bytes where this machine keeps what arrived and asks the system to show them. Overridden
    /// by tests, which have no business launching the user's photo viewer.
    /// </summary>
    protected virtual void OpenPicture(PictureMessage picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        try
        {
            var path = ReceivedPayloads.Save(picture.FileName, picture.Id, picture.Data);
            SetStatus(ReceivedPayloads.TryOpen(path)
                ? $"opening {picture.FileName}"
                : $"no viewer for it: kept in {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Not being able to write the cache is a thing to say, not a reason to lose the window.
            SetStatus($"could not open {picture.FileName}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------- transcript

    /// <summary>
    /// Shows an arriving message if it belongs to the room on screen. The service keeps transcripts
    /// for every room, so a notice about a room this window is not in - the one the host just closed,
    /// for instance - would otherwise be drawn as though it belonged here.
    /// </summary>
    private void ShowIncoming(Message message)
    {
        if (CurrentRoom is not { } room
            || !string.Equals(message.RoomName, room.Name, StringComparison.Ordinal))
        {
            return;
        }

        Append(message);
    }

    /// <summary>
    /// Appends one line to the transcript. Incoming traffic arrives here through
    /// <see cref="IChatService.MessageReceived"/>; something this window sent is appended directly,
    /// because there is no echo back from the service.
    /// </summary>
    protected void Append(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var line = message.SenderNickname.Length == 0
            ? $"[{message.SentAt:HH:mm}] {message.Preview}"
            : $"[{message.SentAt:HH:mm}] {message.SenderNickname}: {message.Preview}";

        rtbChat.SelectionStart = rtbChat.TextLength;
        rtbChat.SelectionLength = 0;
        rtbChat.SelectionColor = message.Kind == MessageKind.JOIN_LEAVE_NOTICE ? Color.Gray : SystemColors.WindowText;
        rtbChat.AppendText(line);

        if (message is PictureMessage picture)
        {
            // The caption is written first and the thumbnail under it, so a picture that turns out not
            // to be a picture at all still leaves a line in the transcript (see RtfPicture).
            openable[line] = picture;

            if (RtfPicture.InlineFor(picture) is { } rtf)
            {
                rtbChat.AppendText(Environment.NewLine);
                AppendRtf(rtf);
            }
        }

        rtbChat.AppendText(Environment.NewLine);
        rtbChat.ScrollToCaret();

        if (message.Kind == MessageKind.JOIN_LEAVE_NOTICE)
        {
            ShowMembers();
        }
    }

    /// <summary>
    /// Writes RTF into the transcript at the end.
    /// <para>
    /// The transcript is read-only, and a read-only RichTextBox refuses an insertion made through
    /// <see cref="RichTextBox.SelectedRtf"/> without complaining - which is how a picture can be sent,
    /// broadcast, received, decoded and then simply not appear. <see cref="RichTextBox.ReadOnly"/> is a
    /// promise about the keyboard rather than about this code, so it is lifted for the insertion and put
    /// back whether or not the insertion worked.
    /// </para>
    /// </summary>
    private void AppendRtf(string rtf)
    {
        var wasReadOnly = rtbChat.ReadOnly;
        rtbChat.ReadOnly = false;

        try
        {
            rtbChat.SelectionStart = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;
            rtbChat.SelectedRtf = rtf;
        }
        finally
        {
            rtbChat.ReadOnly = wasReadOnly;
        }
    }

    // ------------------------------------------------------------------ menu

    /// <summary>Room management is host-only; clients see the menu item but cannot use it.</summary>
    protected void SetRoomEditingEnabled(bool enabled) => renameMenuItem.Enabled = enabled;

    protected virtual void RenameMenuItem_Click(object? sender, EventArgs e)
        => SetStatus("only the host can rename rooms");

    private void ClearChatMenuItem_Click(object? sender, EventArgs e) => rtbChat.Clear();

    private void AboutMenuItem_Click(object? sender, EventArgs e)
        => MessageBox.Show(
            this,
            // Say what this build actually does. Until M4 the radio is never touched, and a demo where
            // the About box claims Bluetooth while the windows talk to 127.0.0.1 is a demo that ends in
            // an argument about whether anything was proven. M4: name the transport in use here.
            "Lantern\nGroup chat between Windows machines.\n\nDevelopment build: messages travel over a "
                + "local loopback transport (127.0.0.1), not Bluetooth yet.",
            "About Lantern",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

    private void QuitMenuItem_Click(object? sender, EventArgs e) => OnQuitRequested();

    /// <summary>File ▸ Quit. The asking happens in <see cref="OnFormClosing"/>, the stopping in <see cref="OnFormClosed"/>.</summary>
    protected virtual void OnQuitRequested() => Close();

    /// <summary>
    /// Whether the user really means it. The host window asks, because everybody else is connected to
    /// it.
    /// </summary>
    protected virtual bool ConfirmClosing() => true;

    /// <summary>
    /// The question is asked here rather than on the menu item, because the ✕ on the title bar and
    /// Alt+F4 come through here as well - and a host closing the window disconnects everybody, which is
    /// not something a keystroke should be able to skip past.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!ConfirmClosing())
        {
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    /// <summary>
    /// The close box is as much an exit as File ▸ Quit, and a chat still running behind a closed window
    /// is a chat the user cannot stop: clients would keep their connection to a host with no face.
    /// </summary>
    /// <remarks>
    /// The wait is the part that matters. A shutdown started fire-and-forget on the way out has no
    /// reason to finish before the process goes, and the host's room list may still be on its way to
    /// the file (F9) - so a room created seconds earlier, or renamed, or deleted, quietly comes back.
    /// The service bounds its own stopping; this only declines to walk away before it is done.
    /// </remarks>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        try
        {
            // Off the UI thread on purpose: awaiting on this thread would need the message loop that is
            // already on its way out, and blocking it directly would deadlock against the same loop.
            Task.Run(() => Service.ShutdownAsync()).Wait(TimeSpan.FromSeconds(15));
        }
        catch (Exception)
        {
            // The window is going away and there is nobody left to tell. The service reports what it can
            // while it is still up; a shutdown that cannot finish is not worth throwing over.
        }
    }

    /// <summary>Scenario 6: the host has gone, so this window has nothing left to do.</summary>
    /// <remarks>
    /// The telling is split out for the same reason as the dialogs on the other windows: a modal sits in
    /// the middle of this method and stops a test reaching the rest of it. What is under test is that the
    /// status bar says why, and that the window steps aside rather than sitting there offering a Send
    /// button that can only fail.
    /// </remarks>
    protected virtual void OnConnectionLost(Exception error)
    {
        SetStatus(error.Message);
        ShowConnectionLost(error);
        Close();
    }

    /// <summary>Telling the person, in a box they have to acknowledge.</summary>
    protected virtual void ShowConnectionLost(Exception error) => ConfirmDialog.ConnectionLost(this);
}
