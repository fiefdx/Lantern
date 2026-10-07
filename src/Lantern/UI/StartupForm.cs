using Lantern.Core;
using Lantern.Domain;
using Lantern.Storage;
using Lantern.Transport;

namespace Lantern.UI;

/// <summary>
/// The window the app opens with (Figure 2): enter a nickname, then either host a chat on this
/// machine or scan for and join one.
/// <para>
/// Whichever branch is taken, this window's job is to build exactly one <see cref="IChatService"/> and
/// hand it to a <see cref="ChatForm"/>. It is the only place in the app that knows whether the chat
/// runs over loopback or Bluetooth, and the only place that decides which role this process has.
/// </para>
/// </summary>
public partial class StartupForm : Form
{
    private IReadOnlyList<ServerEndpoint> discovered = [];

    public StartupForm()
    {
        InitializeComponent();
        txtNickname.Text = Environment.UserName;
        lstServers.Items.Clear();
        SetStatus("enter a nickname, then host or scan");
    }

    /// <summary>The trimmed nickname, valid because the form will not act without one.</summary>
    public string Nickname => ValidationRules.Normalize(txtNickname.Text);

    /// <summary>Chats this process is currently hosting, so the window can stop it.</summary>
    public ChatServer? Hosting { get; private set; }

    // ------------------------------------------------------------- hosting

    private async void BtnStartServer_Click(object? sender, EventArgs e)
    {
        if (!TryValidateNickname())
        {
            return;
        }

        SetBusy(true);
        SetStatus("starting the server…");

        try
        {
            // T4.3 swaps the transport for BtRfcommServer; nothing below this line changes with it.
            // Only the host owns a room list, so only the host is given somewhere to keep it (F9).
            var server = new ChatServer(new BtLoopbackServer(ServerName), ServerName, roomStore: new JsonRoomStore());
            await server.StartAsync();
            Hosting = server;

            var service = new HostChatService(server, Nickname);
            await service.StartAsync();

            Open(new HostForm(service));
        }
        catch (Exception ex)
        {
            await StopHostingAsync();
            SetStatus($"could not host: {ex.Message}");
            SetBusy(false);
        }
    }

    /// <summary>The name clients see in their list: this machine, since no host name is asked for.</summary>
    private static string ServerName => Environment.MachineName;

    // ----------------------------------------------------------- discovering

    private async void BtnScan_Click(object? sender, EventArgs e)
    {
        if (!TryValidateNickname())
        {
            return;
        }

        SetBusy(true);
        lstServers.Items.Clear();
        btnJoin.Enabled = false;
        SetStatus("scanning…");

        try
        {
            discovered = await ServerBrowser.DiscoverAsync();

            foreach (var endpoint in discovered)
            {
                // ServerEndpoint.ToString is its display name, which is what the list should show.
                lstServers.Items.Add(endpoint);
            }

            SetStatus(discovered.Count == 0
                ? "no Lantern chat found — start one, or try again once it is up"
                : $"{discovered.Count} chat(s) found");
        }
        catch (Exception ex)
        {
            SetStatus($"scan failed: {ex.Message}");
        }
        finally
        {
            btnScan.Enabled = true;
        }
    }

    // ---------------------------------------------------------------- joining

    private async void BtnJoin_Click(object? sender, EventArgs e)
    {
        if (!TryValidateNickname())
        {
            return;
        }

        if (lstServers.SelectedItem is not ServerEndpoint endpoint)
        {
            SetStatus("select a chat to join first");
            return;
        }

        SetBusy(true);
        SetStatus($"joining {endpoint}…");

        var client = new ChatClient(Nickname);

        try
        {
            await client.ConnectAsync(endpoint);
            Open(new ClientForm(client));
        }
        catch (Exception ex)
        {
            // Nothing was opened, so this window has to say why it is still here.
            await client.DisposeAsync();
            SetStatus($"could not join: {ex.Message}");
            SetBusy(false);
        }
    }

    private void LstServers_SelectedIndexChanged(object? sender, EventArgs e)
        => btnJoin.Enabled = lstServers.SelectedItem is not null;

    // -------------------------------------------------------------- plumbing

    /// <summary>Blocks the actions and points at the nickname box when it is unusable.</summary>
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

    /// <summary>Greyed out while a connection or a scan is in flight, so it cannot be started twice.</summary>
    private void SetBusy(bool value)
    {
        btnStartServer.Enabled = !value;
        btnScan.Enabled = !value;
        btnJoin.Enabled = false;
        UseWaitCursor = value;
    }

    /// <summary>Swaps this window for the chat window; closing the chat window ends the app.</summary>
    private void Open(ChatForm chat)
    {
        chat.FormClosed += (_, _) => Close();
        chat.Show();
        Hide();
    }

    private async Task StopHostingAsync()
    {
        var server = Hosting;
        Hosting = null;

        if (server is not null)
        {
            await server.DisposeAsync();
        }
    }

    private void SetStatus(string message)
        => lblStatus.Text = message.Length == 0 ? "Status: idle" : $"Status: {message}";
}
