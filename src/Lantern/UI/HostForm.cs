using Lantern.Core;
using Lantern.Domain;

namespace Lantern.UI;

/// <summary>
/// Host flavour of the chat window (Figure 3): the shared layout plus the <b>New</b> and
/// <b>Delete</b> buttons that make this side the one that owns the room list.
/// </summary>
public partial class HostForm : ChatForm
{
    public HostForm(IChatService service)
        : base(service)
    {
        InitializeComponent();

        // The two management buttons live in the base form's header strip so both roles keep an
        // identical layout (compare Figures 3 and 4).
        pnlRoomActions.Controls.Add(btnNew);
        pnlRoomActions.Controls.Add(btnDelete);

        SetRoomEditingEnabled(Service.IsHost);
        btnNew.Enabled = Service.IsHost;
        btnDelete.Enabled = Service.IsHost;
    }

    /// <summary>"New" → <see cref="NewRoomDialog"/> → <see cref="IChatService.CreateRoomAsync"/>.</summary>
    private async void OnNewRoomClick(object? sender, EventArgs e)
    {
        // Duplicate names are refused by the dialog as they are typed, not after the OK click.
        using var dialog = new NewRoomDialog("New Room", string.Empty, IsDuplicate);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await RunAsync(() => Service.CreateRoomAsync(dialog.RoomName));
    }

    /// <summary>"Delete" → <see cref="ConfirmDialog.ConfirmDeleteRoom"/> → <see cref="IChatService.DeleteRoomAsync"/>.</summary>
    private async void OnDeleteRoomClick(object? sender, EventArgs e)
    {
        if (SelectedRoom is not { } room)
        {
            SetStatus("select a room first");
            return;
        }

        if (!ConfirmDialog.ConfirmDeleteRoom(this, room))
        {
            return;
        }

        await RunAsync(() => Service.DeleteRoomAsync(room));
    }

    /// <summary>Edit ▸ Rename: the same dialog, pre-filled (F3). Leaving the name alone is a no-op.</summary>
    protected override async void RenameMenuItem_Click(object? sender, EventArgs e)
    {
        if (SelectedRoom is not { } current)
        {
            SetStatus("select a room first");
            return;
        }

        using var dialog = new NewRoomDialog("Rename Room", current, name =>
            name == current ? null : IsDuplicate(name));

        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.RoomName == current)
        {
            return;
        }

        await RunAsync(() => Service.RenameRoomAsync(current, dialog.RoomName));
    }

    /// <summary>A second room of the same name is refused before it reaches the service.</summary>
    private string? IsDuplicate(string name)
        => ValidationRules.IsDuplicateRoomName(name, Service.Rooms.Select(r => r.Name))
            ? "A room with that name already exists."
            : null;

    /// <summary>File ▸ Quit asks first, because everybody else is connected to this window.</summary>
    protected override void OnQuitRequested()
    {
        if (ConfirmDialog.ConfirmQuit(this))
        {
            base.OnQuitRequested();
        }
    }
}
