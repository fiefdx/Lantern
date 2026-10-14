namespace Lantern.UI;

/// <summary>
/// The OK/Cancel confirmation used before anything destructive (Figure 5):
/// <see cref="ConfirmQuit"/>, <see cref="ConfirmDeleteRoom"/> and
/// <see cref="ConfirmLeaveRoom"/>. Returns <c>true</c> only on OK.
/// </summary>
public partial class ConfirmDialog : Form
{
    /// <summary>
    /// For the design surface, which has no question to ask and still has to draw the box. Asked for by
    /// anything else this refuses: a confirmation without its question is not a confirmation, and the
    /// questions live in <see cref="ConfirmQuit"/> and friends - see <see cref="DesignTime"/>.
    /// </summary>
    public ConfirmDialog()
    {
        // Asked first, while nothing has been built yet: outside the design surface there is no such thing as
        // a confirmation that has not been given its question - see <see cref="DesignTime"/>.
        var (title, message) = DesignTime.Confirmation(nameof(ConfirmDialog));

        InitializeComponent();

        Present(title, message);
    }

    private ConfirmDialog(string title, string message)
    {
        InitializeComponent();

        Present(title, message);
    }

    private void Present(string title, string message)
    {
        AppIcon.Apply(this);

        Text = title;
        lblMessage.Text = message;
    }

    /// <summary>Shows the dialog modally to <paramref name="owner"/>.</summary>
    private static bool Ask(IWin32Window? owner, string title, string message)
    {
        using var dialog = new ConfirmDialog(title, message);
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }

    /// <summary>Scenario 6 — the host is about to shut the whole server down.</summary>
    public static bool ConfirmQuit(IWin32Window? owner)
        // The question says where they end up, because the word "Quit" used to leave that out: closing this
        // window stops everybody's chat and goes back to the start screen — it is not the program's ✕.
        => Ask(
            owner,
            "Quit Server",
            "Stop the chat and go back? Everyone still connected will be told the host has gone.");

    /// <summary>Scenario 3 — a room is about to be removed from rooms.json.</summary>
    public static bool ConfirmDeleteRoom(IWin32Window? owner, string roomName)
        => Ask(owner, "Delete Room", $"Delete room \"{roomName}\"? Anyone inside it will be removed.");

    /// <summary>Scenario 1 / 3 — a client is about to leave its current room.</summary>
    public static bool ConfirmLeaveRoom(IWin32Window? owner, string roomName)
        => Ask(owner, "Leave Room", $"Leave room \"{roomName}\"?");

    /// <summary>Generic variant for anything else that needs a yes/no.</summary>
    public static bool Confirm(IWin32Window? owner, string title, string message)
        => Ask(owner, title, message);

    /// <summary>Scenario 5 — the host closed the server while a client was connected.</summary>
    public static void ConnectionLost(IWin32Window? owner)
        => MessageBox.Show(owner, "The server closed the connection.", "Connection lost",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
