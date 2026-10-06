namespace Lantern.UI;

/// <summary>
/// Client flavour of the chat window (Figure 4): the room list is read-only (no
/// New/Delete) and a single <b>Leave</b> button is added instead.
/// </summary>
public partial class ClientForm : ChatForm
{
    public ClientForm(string nickname)
        : base(nickname)
    {
        InitializeComponent();

        pnlRoomActions.Controls.Add(btnLeave);

        UpdateTitle("client");
        SetRoomEditingEnabled(false);
        LoadDemoData();
    }

    /// <summary>Raised when the user confirms leaving the current room.</summary>
    public event EventHandler<string>? LeaveRoomRequested;

    private void LoadDemoData()
    {
        SetRooms(["Room 1", "Room 2", "Room 3"]);
        SetMembers(["User 1", Nickname]);
        AppendMessage(string.Empty, $"{Nickname} - join this chat", isNotice: true);
        AppendMessage("User 1", "hello");
        SetStatus("connected");
    }

    private void OnLeaveClick(object? sender, EventArgs e)
    {
        // Wired to the confirm dialog in T0.6.
        if (SelectedRoom is { } room)
        {
            LeaveRoomRequested?.Invoke(this, room);
        }
    }
}
