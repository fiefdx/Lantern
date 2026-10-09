using Lantern.Core;
using Lantern.Domain;

namespace Lantern.UI;

/// <summary>
/// Host flavour of the chat window (Figure 3): the shared layout plus the <b>New</b> and
/// <b>Delete</b> buttons that make this side the one that owns the room list.
/// </summary>
public partial class HostForm : ChatForm
{
    /// <summary>
    /// For the design surface, which builds the window in order to draw it - see <see cref="DesignTime"/>.
    /// Like every window here it calls <c>InitializeComponent</c> itself and then joins the other constructor
    /// in <see cref="AddRoomButtons"/>, so what is being designed is what runs.
    /// </summary>
    public HostForm()
        : base(DesignTime.ChatService(isHost: true, nameof(HostForm)))
    {
        InitializeComponent();

        AddRoomButtons();
    }

    public HostForm(IChatService service)
        : base(service)
    {
        InitializeComponent();

        AddRoomButtons();
    }

    private void AddRoomButtons()
    {
        // The two management buttons live in the base form's header strip so both roles keep an
        // identical layout (compare Figures 3 and 4).
        pnlRoomActions.Controls.Add(btnNew);
        pnlRoomActions.Controls.Add(btnDelete);

        SetRoomEditingEnabled(Service.IsHost);
        btnNew.Enabled = Service.IsHost;
        btnDelete.Enabled = Service.IsHost;
    }

    /// <summary>"New" → <see cref="AskForRoomName"/> → <see cref="IChatService.CreateRoomAsync"/>.</summary>
    private async void OnNewRoomClick(object? sender, EventArgs e)
    {
        // Duplicate names are refused by the dialog as they are typed, not after the OK click.
        var name = AskForRoomName("New Room", string.Empty, IsDuplicate);

        if (name is null)
        {
            return;
        }

        await RunAsync(() => Service.CreateRoomAsync(name));
    }

    /// <summary>"Delete" → <see cref="ConfirmDeleteRoom"/> → <see cref="IChatService.DeleteRoomAsync"/>.</summary>
    private async void OnDeleteRoomClick(object? sender, EventArgs e)
    {
        if (SelectedRoom is not { } room)
        {
            SetStatus("select a room first");
            return;
        }

        if (!ConfirmDeleteRoom(room.Name))
        {
            return;
        }

        await RunAsync(() => Service.DeleteRoomAsync(room.Id));
    }

    /// <summary>Edit ▸ Rename: the same dialog, pre-filled (F3). Leaving the name alone is a no-op.</summary>
    protected override async void RenameMenuItem_Click(object? sender, EventArgs e)
    {
        if (SelectedRoom is not { } current)
        {
            SetStatus("select a room first");
            return;
        }

        var renamed = AskForRoomName("Rename Room", current.Name, name => name == current.Name ? null : IsDuplicate(name));

        if (renamed is null || renamed == current.Name)
        {
            return;
        }

        await RunAsync(() => Service.RenameRoomAsync(current.Id, renamed));
    }

    /// <summary>
    /// Asks for a room name, returning null if the user cancelled.
    /// </summary>
    /// <remarks>
    /// A method rather than a dialog inlined in the click handler so that a test can answer instead of a
    /// person. The interesting parts are the button, the service call and the list refreshing; a modal
    /// sitting in front of them would stop any of that being tested at all. The dialog itself has its own
    /// tests (<c>NewRoomDialogTests</c>).
    /// </remarks>
    protected virtual string? AskForRoomName(string title, string current, Func<string, string?> validate)
    {
        using var dialog = new NewRoomDialog(title, current, validate);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.RoomName : null;
    }

    /// <summary>Asks before destroying a room, because everybody inside is about to be put out.</summary>
    protected virtual bool ConfirmDeleteRoom(string roomName) => ConfirmDialog.ConfirmDeleteRoom(this, roomName);

    /// <summary>A second room of the same name is refused before it reaches the service.</summary>
    private string? IsDuplicate(string name)
        => ValidationRules.IsDuplicateRoomName(name, Service.Rooms.Select(r => r.Name))
            ? "A room with that name already exists."
            : null;

    /// <summary>
    /// Closing this window asks first - by menu, by the ✕ or by Alt+F4 alike - because everybody else is
    /// connected to it, and because what it stops is not just this window's chat. Going back is the point
    /// afterwards: the start screen, where another chat can be hosted or joined without restarting the
    /// program (and where a pairing that has since died can be tried again).
    /// </summary>
    protected override bool ConfirmClosing() => ConfirmQuit();

    /// <summary>The asking part of quitting, separate for the same reason as <see cref="AskForRoomName"/>.</summary>
    protected virtual bool ConfirmQuit() => ConfirmDialog.ConfirmQuit(this);
}
