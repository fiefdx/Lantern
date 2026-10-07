namespace Lantern.UI;

/// <summary>
/// Host flavour of the chat window (Figure 3): same layout as the client plus the
/// <b>New</b> and <b>Delete</b> room-management buttons.
/// </summary>
public partial class HostForm : ChatForm
{
    public HostForm(string nickname)
        : base(nickname)
    {
        InitializeComponent();

        // The two management buttons live in the base form's header strip so the
        // two roles keep an identical layout (see Figures 3 and 4).
        pnlRoomActions.Controls.Add(btnNew);
        pnlRoomActions.Controls.Add(btnDelete);

        UpdateTitle("host");
        SetRoomEditingEnabled(true);
        LoadDemoData();
    }

    /// <summary>Raised when the user confirms deleting the selected room.</summary>
    public event EventHandler<string>? DeleteRoomRequested;

    /// <summary>Raised when the user supplies a name for a new room.</summary>
    public event EventHandler<string>? CreateRoomRequested;

    /// <summary>Raised when the user supplies a new name for the selected room.</summary>
    public event EventHandler<(string Current, string NewName)>? RenameRoomRequested;

    private void LoadDemoData()
    {
        SetRooms(["Room 1", "Room 2", "Room 3"]);
        SetMembers([Nickname, "User 2", "User 3"]);
        AppendMessage(string.Empty, "User 2 - join this chat", isNotice: true);
        AppendMessage("User 3", "hi");
        AppendMessage(Nickname, "hi");
        SetStatus("server online");
    }

    /// <summary>"New" → <see cref="NewRoomDialog"/> → service call.</summary>
    private void OnNewRoomClick(object? sender, EventArgs e)
    {
        using var dialog = new NewRoomDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            CreateRoomRequested?.Invoke(this, dialog.RoomName);
        }
    }

    /// <summary>"Delete" → confirm → service call.</summary>
    private void OnDeleteRoomClick(object? sender, EventArgs e)
    {
        if (SelectedRoom is not { } room)
        {
            SetStatus("select a room first");
            return;
        }

        if (ConfirmDialog.ConfirmDeleteRoom(this, room))
        {
            DeleteRoomRequested?.Invoke(this, room);
        }
    }

    /// <summary>Same dialog as "New", pre-filled with the current name.</summary>
    protected override void RenameMenuItem_Click(object? sender, EventArgs e)
    {
        if (SelectedRoom is not { } current)
        {
            SetStatus("select a room first");
            return;
        }

        using var dialog = new NewRoomDialog("Rename Room", current);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.RoomName != current)
        {
            RenameRoomRequested?.Invoke(this, (current, dialog.RoomName));
        }
    }

    /// <summary>File ▸ Quit asks before killing the server (scenario 6).</summary>
    protected override void OnQuitRequested()
    {
        if (ConfirmDialog.ConfirmQuit(this))
        {
            base.OnQuitRequested();
        }
    }
}
