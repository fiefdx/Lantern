namespace Lantern.UI;

/// <summary>
/// The main chat window, shared by both roles (Figures 3 and 4): rooms on the left,
/// the transcript in the middle, the member list on the right, and the message box
/// with <b>Send</b> / <b>Share</b> along the bottom.
/// </summary>
/// <remarks>
/// This base class knows nothing about Bluetooth or sockets — from T0.7 onwards it is
/// driven only through <c>IChatService</c>. <see cref="HostForm"/> adds New/Delete,
/// <see cref="ClientForm"/> adds Leave.
/// </remarks>
public abstract partial class ChatForm : Form
{
    private bool busy;

    /// <summary>Raised when the user picks a different room in the left panel.</summary>
    public event EventHandler<string?>? RoomSelected;

    /// <summary>Raised when the user asks to send <see cref="PendingText"/> to the room.</summary>
    public event EventHandler<string>? SendRequested;

    /// <summary>Raised by the Share button; the role form decides picture vs file.</summary>
    public event EventHandler? ShareRequested;

    /// <summary>Raised by File ▸ Quit; the role form confirms and shuts the service down.</summary>
    public event EventHandler? QuitRequested;

    protected ChatForm(string nickname)
    {
        Nickname = nickname;
        InitializeComponent();
        Load += (_, _) => RefreshSendEnabled();
        txtMessage.TextChanged += (_, _) => RefreshSendEnabled();
    }

    /// <summary>Nickname typed on the startup window; "me" in the transcript.</summary>
    public string Nickname { get; }

    /// <summary>The text currently typed in the message box.</summary>
    protected string PendingText => txtMessage.Text;

    /// <summary>The room highlighted in the left panel, or null when nothing is selected.</summary>
    protected string? SelectedRoom => lstRooms.SelectedItem as string;

    /// <summary>Message box, so derived forms can clear it after a successful send.</summary>
    protected void ClearPendingText() => txtMessage.Clear();

    /// <summary>Replaces the left panel; selection is kept when the room still exists.</summary>
    protected void SetRooms(IEnumerable<string> names, string? select = null)
    {
        var previous = lstRooms.SelectedItem as string;
        lstRooms.BeginUpdate();
        lstRooms.Items.Clear();
        foreach (var name in names)
        {
            lstRooms.Items.Add(name);
        }

        lstRooms.EndUpdate();

        var wanted = select ?? previous;
        var index = wanted is null ? -1 : lstRooms.Items.IndexOf(wanted);
        lstRooms.SelectedIndex = index >= 0 ? index : (lstRooms.Items.Count > 0 ? 0 : -1);
    }

    /// <summary>Replaces the right panel and keeps the "(n)" count in the header.</summary>
    protected void SetMembers(IEnumerable<string> nicknames)
    {
        var names = nicknames.ToList();
        lstMembers.BeginUpdate();
        lstMembers.Items.Clear();
        foreach (var name in names)
        {
            lstMembers.Items.Add(name);
        }

        lstMembers.EndUpdate();
        lblMembersHeader.Text = $"Members ({names.Count})";
    }

    /// <summary>Appends one transcript entry; sender is bolded like the wireframe.</summary>
    protected void AppendMessage(string sender, string body, bool isNotice = false)
    {
        if (sender.Length > 0)
        {
            rtbChat.SelectionStart = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;
            rtbChat.SelectionFont = new Font(rtbChat.Font, isNotice ? FontStyle.Regular : FontStyle.Bold);
            rtbChat.SelectionColor = isNotice ? SystemColors.GrayText : SystemColors.ControlText;
            rtbChat.AppendText($"{sender}{Environment.NewLine}");
        }

        rtbChat.SelectionStart = rtbChat.TextLength;
        rtbChat.SelectionLength = 0;
        rtbChat.SelectionFont = rtbChat.Font;
        rtbChat.SelectionColor = isNotice ? SystemColors.GrayText : SystemColors.ControlText;
        rtbChat.AppendText($"{body}{Environment.NewLine}");
        rtbChat.ScrollToCaret();
    }

    /// <summary>Writes the status bar; empty text restores "Status: idle".</summary>
    protected void SetStatus(string message)
        => lblStatus.Text = string.IsNullOrWhiteSpace(message) ? "Status: idle" : $"Status: {message}";

    /// <summary>
    /// Disables <b>Send</b> while a send is in flight (double-send guard, T2.7) and
    /// whenever the message box is empty or no room is selected.
    /// </summary>
    protected void SetBusy(bool busy)
    {
        this.busy = busy;
        RefreshSendEnabled();
    }

    private bool CanSend => !busy && SelectedRoom is not null && txtMessage.Text.Trim().Length > 0;

    private void RefreshSendEnabled()
    {
        btnSend.Enabled = CanSend;
        btnShare.Enabled = !busy && SelectedRoom is not null;
    }

    private void LstRooms_SelectedIndexChanged(object? sender, EventArgs? e)
    {
        RefreshSendEnabled();
        RoomSelected?.Invoke(this, SelectedRoom);
    }

    private void BtnSend_Click(object? sender, EventArgs? e) => RequestSend();

    private void TxtMessage_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            RequestSend();
        }
    }

    private void RequestSend()
    {
        var text = txtMessage.Text;
        if (SelectedRoom is null || text.Trim().Length == 0)
        {
            return;
        }

        SetBusy(true);
        try
        {
            if (SendRequested is null)
            {
                // Placeholder until the real IChatService is bound (T0.7): echo locally.
                AppendMessage(Nickname, text);
                ClearPendingText();
            }
            else
            {
                SendRequested.Invoke(this, text);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BtnShare_Click(object? sender, EventArgs? e)
    {
        if (SelectedRoom is null)
        {
            return;
        }

        SetBusy(true);
        try
        {
            ShareRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void QuitMenuItem_Click(object? sender, EventArgs? e) => QuitRequested?.Invoke(this, EventArgs.Empty);

    private void ClearChatMenuItem_Click(object? sender, EventArgs? e) => rtbChat.Clear();

    private void AboutMenuItem_Click(object? sender, EventArgs? e)
        => MessageBox.Show(this, "Lantern — a local Bluetooth group chat for Windows.", "About Lantern",
            MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>Overridden by the host form; the base form exposes no room editing.</summary>
    protected virtual void RenameMenuItem_Click(object? sender, EventArgs e)
        => MessageBox.Show(this, "Only the host can rename a room.", "Rename room",
            MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>Called by role forms once the service has been bound.</summary>
    protected void UpdateTitle(string role) => Text = $"Lantern - {role}";

    /// <summary>Enables or greys out room editing (host only, scenario 3).</summary>
    protected void SetRoomEditingEnabled(bool enabled)
    {
        renameMenuItem.Enabled = enabled;
    }
}
