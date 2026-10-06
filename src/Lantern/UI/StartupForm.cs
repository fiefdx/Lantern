using Lantern.Domain;

namespace Lantern.UI;

/// <summary>
/// The window the app opens with (Figure 2): enter a nickname, then either host a
/// chat on this machine or scan for and join a nearby host.
/// </summary>
public partial class StartupForm : Form
{
    /// <summary>Placeholder rows shown in the server list before a real scan.</summary>
    private static readonly string[] ServerListPlaceholders =
    [
        "Host A (1.2.3.4)",
        "Host B (5.6.7.8)",
    ];

    public StartupForm()
    {
        InitializeComponent();
        txtNickname.Text = Environment.UserName;
        lstServers.Items.AddRange(ServerListPlaceholders);
        SetStatus("idle");
    }

    /// <summary>The trimmed nickname, valid because the form will not close without one.</summary>
    public string Nickname => ValidationRules.Normalize(txtNickname.Text);

    private void BtnStartServer_Click(object? sender, EventArgs e)
    {
        if (!TryValidateNickname())
        {
            return;
        }

        // T0.7 replaces the construction with a ChatServer-backed HostForm.
        Open(new HostForm(Nickname));
    }

    private void BtnScan_Click(object? sender, EventArgs e)
    {
        if (!TryValidateNickname())
        {
            return;
        }

        // T2.6 / T4.4 replace this with ServerBrowser.DiscoverAsync.
        SetStatus($"scanning as \"{Nickname}\" (not wired up yet)");
    }

    private void BtnJoin_Click(object? sender, EventArgs e)
    {
        if (!TryValidateNickname())
        {
            return;
        }

        if (lstServers.SelectedItem is null)
        {
            SetStatus("select a server first");
            return;
        }

        // T0.7 replaces the construction with a ChatClient-backed ClientForm.
        Open(new ClientForm(Nickname));
    }

    private void LstServers_SelectedIndexChanged(object? sender, EventArgs e)
        => btnJoin.Enabled = lstServers.SelectedItem is not null;

    /// <summary>Blocks the action and points at the nickname box when it is unusable.</summary>
    private bool TryValidateNickname()
    {
        var error = ValidationRules.NicknameError(txtNickname.Text);
        if (error is null)
        {
            SetStatus(string.Empty);
            return true;
        }

        txtNickname.Focus();
        txtNickname.SelectAll();
        SetStatus(error);
        return false;
    }

    /// <summary>Swaps this window for the chat window; closing the chat window ends the app.</summary>
    private void Open(ChatForm chat)
    {
        chat.FormClosed += (_, _) => Close();
        chat.Show();
        Hide();
    }

    private void SetStatus(string message)
        => lblStatus.Text = message.Length == 0 ? "Status: idle" : $"Status: {message}";
}
