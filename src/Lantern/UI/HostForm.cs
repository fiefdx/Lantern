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

    private void LoadDemoData()
    {
        SetRooms(["Room 1", "Room 2", "Room 3"]);
        SetMembers([Nickname, "User 2", "User 3"]);
        AppendMessage(string.Empty, "User 2 - join this chat", isNotice: true);
        AppendMessage("User 3", "hi");
        AppendMessage(Nickname, "hi");
        SetStatus("server online");
    }

    private void OnNewRoomClick(object? sender, EventArgs e)
    {
        // Wired to NewRoomDialog in T0.6.
        CreateRoomRequested?.Invoke(this, string.Empty);
    }

    private void OnDeleteRoomClick(object? sender, EventArgs e)
    {
        // Wired to the confirm dialog in T0.6.
        if (SelectedRoom is { } room)
        {
            DeleteRoomRequested?.Invoke(this, room);
        }
    }

    /// <summary>Rename uses the same dialog as New, pre-filled, from T0.6 onwards.</summary>
    protected override void RenameMenuItem_Click(object? sender, EventArgs e)
    {
        // Wired to NewRoomDialog in pre-filled (rename) mode in T0.6.
    }
}
