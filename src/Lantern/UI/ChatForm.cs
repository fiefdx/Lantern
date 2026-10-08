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
        Service.IncomingFile += (_, job) => OnUi(() => ReceiveFile(job));
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

    private void BtnShare_Click(object? sender, EventArgs e) => _ = ShareFileAsync();

    private async Task ShareFileAsync()
    {
        if (CurrentRoom is not { } room)
        {
            SetStatus("select a room first");
            return;
        }

        using var picker = new OpenFileDialog
        {
            Title = "Send File",
            Filter = "All files (*.*)|*.*",
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var file = new FileInfo(picker.FileName);
        if (!file.Exists)
        {
            SetStatus("that file could not be read");
            return;
        }

        var message = new FileMessage(Service.Nickname, room.Name, file.Name, file.Length);
        await RunAsync(async () =>
        {
            await Service.SendAsync(message);
            Append(message);
            SetStatus($"sending {file.Name}");
        });
    }

    // ------------------------------------------------------------- receiving

    /// <summary>Scenario 6: name, size and sender, then Accept or Decline.</summary>
    private void ReceiveFile(FileTransferJob job)
    {
        if (!ConfirmDialog.Confirm(this, "Incoming File", $"{job.SenderNickname} wants to send \"{job.FileName}\" ({FileSize.Describe(job.SizeBytes)}). Accept it?"))
        {
            job.CancelTransfer();
            return;
        }

        using var picker = new SaveFileDialog { Title = "Save Incoming File", FileName = job.FileName };
        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            job.CancelTransfer();
            return;
        }

        _ = RunAsync(async () =>
        {
            await Service.SaveReceivedFileAsync(job, picker.FileName);
            Append(new FileMessage(job.SenderNickname, job.RoomName, job.FileName, job.SizeBytes));
            SetStatus($"saved {job.FileName}");
        });
    }

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
