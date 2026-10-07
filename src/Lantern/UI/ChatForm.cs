using Lantern.Core;
using Lantern.Domain;

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
        if (!ConfirmDialog.Confirm(this, "Incoming File", $"{job.SenderNickname} wants to send \"{job.FileName}\" ({FriendlySize(job.SizeBytes)}). Accept it?"))
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

    private static string FriendlySize(long bytes)
        => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
        };

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
        rtbChat.AppendText(Environment.NewLine);
        rtbChat.ScrollToCaret();

        if (message.Kind == MessageKind.JOIN_LEAVE_NOTICE)
        {
            ShowMembers();
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

    /// <summary>Scenario 5: the host has gone, so this window has nothing left to do.</summary>
    protected virtual void OnConnectionLost(Exception error)
    {
        SetStatus(error.Message);
        ConfirmDialog.ConnectionLost(this);
        Close();
    }
}
