using Lantern.Core;

namespace Lantern.UI;

/// <summary>
/// Client flavour of the chat window (Figure 4): the room list is read-only — no New or Delete —
/// and a single <b>Leave</b> button replaces them.
/// </summary>
public partial class ClientForm : ChatForm
{
    public ClientForm(IChatService service)
        : base(service)
    {
        InitializeComponent();

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

        if (!ConfirmDialog.ConfirmLeaveRoom(this, room))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await Service.JoinRoomAsync(string.Empty);
            LeaveRoom(announce: false);
            SetStatus("left the room");
        });
    }

    /// <summary>Renaming is the host's job, so the menu item stays disabled (see the base form).</summary>
    protected override void RenameMenuItem_Click(object? sender, EventArgs e) => SetStatus("only the host can rename rooms");
}
