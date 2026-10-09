using Lantern.Core;

namespace Lantern.UI;

/// <summary>
/// Client flavour of the chat window (Figure 4): the room list is read-only — no New or Delete —
/// and a single <b>Leave</b> button replaces them.
/// </summary>
public partial class ClientForm : ChatForm
{
    /// <summary>
    /// For the design surface, which builds the window in order to draw it - see <see cref="DesignTime"/>.
    /// Calls <c>InitializeComponent</c> itself, like every other window, and shares the rest of its
    /// construction with the constructor the program uses.
    /// </summary>
    public ClientForm()
        : base(DesignTime.ChatService(isHost: false, nameof(ClientForm)))
    {
        InitializeComponent();

        AddRoomButton();
    }

    public ClientForm(IChatService service)
        : base(service)
    {
        InitializeComponent();

        AddRoomButton();
    }

    private void AddRoomButton()
    {
        pnlRoomActions.Controls.Add(btnLeave);

        SetRoomEditingEnabled(false);
    }

    /// <summary>"Leave" → <see cref="ConfirmDialog.ConfirmLeaveRoom"/> → join nothing.</summary>
    private async void OnLeaveClick(object? sender, EventArgs e)
    {
        if (SelectedRoom is not { } room)
        {
            SetStatus("you are not in a room");
            return;
        }

        if (!ConfirmLeaveRoom(room.Name))
        {
            return;
        }

        await RunAsync(async () =>
        {
            // Joining "nothing" is how leaving travels on the wire; the notice the room sees is the
            // host's doing, not this window writing its own epitaph (F11).
            await Service.JoinRoomAsync(null);
            LeaveRoom(announce: false);
            SetStatus("left the room");
        });
    }

    /// <summary>
    /// The asking part of leaving, separate from the click handler so a test can answer instead of a
    /// person. See <see cref="HostForm.AskForRoomName"/> for the same seam.
    /// </summary>
    protected virtual bool ConfirmLeaveRoom(string roomName) => ConfirmDialog.ConfirmLeaveRoom(this, roomName);

    /// <summary>Renaming is the host's job, so the menu item stays disabled (see the base form).</summary>
    protected override void RenameMenuItem_Click(object? sender, EventArgs e) => SetStatus("only the host can rename rooms");
}
