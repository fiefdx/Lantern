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
    private readonly ITransportFactory transport;
    private readonly IKnownHosts? knownHosts;

    private IReadOnlyList<ServerEndpoint> discovered = [];
    private CancellationTokenSource? scan;

    /// <summary>The window a person sees: it decides the transport itself, from the radio and the environment.</summary>
    public StartupForm()
        : this(Transports.ForStartup(Transports.Wanted(Environment.GetCommandLineArgs())), new JsonKnownHostsStore())
    {
    }

    /// <summary>
    /// The same window over a transport somebody else chose, which is how the tests run it and how a
    /// second copy on one laptop is driven through the checklist (T4.1 D1).
    /// </summary>
    public StartupForm(ITransportFactory transport, IKnownHosts? knownHosts = null)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.knownHosts = knownHosts;

        InitializeComponent();
        txtNickname.Text = Environment.UserName;
        lstServers.Items.Clear();

        // The transport is stated before anything is clicked. A machine on loopback will never find a
        // chat, and it should say so rather than leave somebody pressing Scan.
        SetStatus($"{transport.Description} — enter a nickname, then host or scan");
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
            // Only the host owns a room list, so only the host is given somewhere to keep it (F9).
            var server = new ChatServer(transport.NewServer(ServerName), ServerName, roomStore: new JsonRoomStore());
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
            Resume();
        }
    }

    /// <summary>The name clients see in their list: this machine, since no host name is asked for.</summary>
    private static string ServerName => Environment.MachineName;

    // ----------------------------------------------------------- discovering

    /// <summary>
    /// Scan, and while a scan is in flight, stop it. There is no Stop button in the figure, and there is
    /// no need for one: the button that started it is the one that says "Scanning…", and a radio inquiry
    /// takes long enough that the person standing there may change their mind.
    /// </summary>
    private async void BtnScan_Click(object? sender, EventArgs e)
    {
        if (scan is { } running)
        {
            running.Cancel();
            SetStatus("stopping the scan…");
            return;
        }

        if (!TryValidateNickname())
        {
            return;
        }

        scan = new CancellationTokenSource();
        lstServers.Items.Clear();
        SetBusy(true);
        btnScan.Enabled = true;              // still clickable, because it is now the cancel
        btnScan.Text = "Scanning…";
        SetStatus($"scanning {transport.Name}…");

        var client = transport.NewClient();

        try
        {
            discovered = await ServerBrowser.DiscoverAsync(
                ServerBrowser.DefaultTimeout,
                client,
                knownHosts,
                scan.Token);

            foreach (var endpoint in discovered)
            {
                // ServerEndpoint.ToString is its display name, which is what the list should show.
                lstServers.Items.Add(endpoint);
            }

            SetStatus(discovered.Count switch
            {
                0 when scan.IsCancellationRequested => "scan stopped",
                0 => "no Lantern chat found — is the other machine hosting, and is it paired?",
                _ when scan.IsCancellationRequested => $"scan stopped with {discovered.Count} chat(s) listed",
                _ => $"{discovered.Count} chat(s) found",
            });
        }
        catch (Exception ex)
        {
            SetStatus($"scan failed: {ex.Message}");
        }
        finally
        {
            (client as IDisposable)?.Dispose();
            EndScan();
        }
    }

    /// <summary>Puts the Scan button back and lets the window be used again.</summary>
    private void EndScan()
    {
        scan?.Dispose();
        scan = null;

        btnScan.Text = "Scan";
        Resume();
    }

    /// <summary>Greyed things back on, and hands the Join button back if there is still something selected.</summary>
    private void Resume()
    {
        SetBusy(false);
        btnJoin.Enabled = lstServers.SelectedItem is not null;
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

        var client = new ChatClient(Nickname, transport.NewClient());

        try
        {
            await client.ConnectAsync(endpoint);

            // The machines worth offering next time are the ones this laptop has actually joined, since a
            // host is usually paired rather than discoverable (T4.1 D2).
            knownHosts?.Remember(endpoint);

            Open(new ClientForm(client));
        }
        catch (Exception ex)
        {
            // Nothing was opened, so this window has to say why it is still here.
            await client.DisposeAsync();
            SetStatus($"could not join: {ex.Message}");

            // The row is still there and still selected: a machine that did not answer a minute ago is
            // worth another press, and making the person choose it again is a penalty for the friend's
            // switched-off laptop.
            Resume();
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
        lstServers.Enabled = !value;
        txtNickname.Enabled = !value;
        UseWaitCursor = value;
    }

    /// <summary>
    /// Swaps this window for the chat window; closing the chat window ends the app. Split out and
    /// overridable so a test can be handed the window that was built rather than hunt the screen for it.
    /// </summary>
    protected virtual void ShowChat(ChatForm chat)
    {
        chat.FormClosed += (_, _) => Close();
        chat.Show();
        Hide();
    }

    private void Open(ChatForm chat)
    {
        // F10 belongs to the composed program rather than to the window: a ChatForm on its own - in a test,
        // or in whatever gets built next - should not be making sounds that nobody asked for.
        chat.AttachSounds(new SoundNotifier());

        ShowChat(chat);
        EndScan();
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
