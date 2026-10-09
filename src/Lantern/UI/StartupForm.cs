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

    /// <summary>The service the chat window currently away was built on, so it can be disposed once that window has gone.</summary>
    private IChatService? lastService;

    /// <summary>
    /// The last chat, still packing up. A Bluetooth channel can take seconds to let go, and that is a reason
    /// the next chat waits a moment, not a reason the window somebody just closed stays gone for that long.
    /// </summary>
    private Task? packingUp;

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

        if (!await LetsStartAnotherChatAsync())
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

            Open(NewHostWindow(service), service);
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

        if (!await LetsStartAnotherChatAsync())
        {
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

            Open(NewClientWindow(client), client);
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

    /// <summary>The window for a chat this machine hosts. Overridable because which window to show is a choice, not a law.</summary>
    protected virtual ChatForm NewHostWindow(IChatService service) => new HostForm(service);

    /// <summary>The window for a chat this machine has joined.</summary>
    protected virtual ChatForm NewClientWindow(IChatService service) => new ClientForm(service);

    /// <summary>
    /// Swaps this window for the chat window. Closing the chat window brings this one back: quitting a chat
    /// and quitting Lantern are different things, and the one that needs a decision — pairing, a host that
    /// is not switched on, a nickname typed wrong — should be reachable again without starting the program
    /// from the beginning.
    /// </summary>
    protected virtual void ShowChat(ChatForm chat)
    {
        chat.FormClosed += (_, _) => ChatWindowClosed(chat);
        chat.Show();
        Hide();
    }

    /// <summary>
    /// The chat window has gone, so this one is the screen again. Note what does <i>not</i> happen here:
    /// nothing waits for the service to finish stopping, or for the radio to finish letting go. That work is
    /// started, remembered in <see cref="packingUp"/>, and waited on by the two things that genuinely need
    /// it — starting another chat, and the process exiting.
    /// </summary>
    protected virtual void ChatWindowClosed(ChatForm chat)
    {
        var service = lastService;
        var server = Hosting;
        lastService = null;
        Hosting = null;

        packingUp = Task.Run(() => PackUpAsync(chat.StoppingChat, service, server));

        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        txtNickname.Focus();

        SetStatus(chat.ClosingExplanation is { Length: > 0 } reason
            ? $"the chat ended: {reason}"
            : "the chat is closed — host one or join one");

        // Back to being usable, whatever the buttons were doing when the chat window took them away.
        EndScan();
    }

    /// <summary>Finishing with one chat: the window's own stopping, then the service it was built on, then the server.</summary>
    private static async Task PackUpAsync(Task stopping, IChatService? service, ChatServer? server)
    {
        await stopping;

        try
        {
            if (service is IAsyncDisposable asynchronously)
            {
                await asynchronously.DisposeAsync();
            }
            else
            {
                (service as IDisposable)?.Dispose();
            }
        }
        catch (Exception)
        {
            // A service that cannot be put away has nowhere left to be put, and the person is back at the
            // start screen with a working set of buttons.
        }

        try
        {
            if (server is not null)
            {
                await server.DisposeAsync();
            }
        }
        catch (Exception)
        {
            // As above. The transport releases what it can; a second chat is built from a new one anyway.
        }
    }

    /// <summary>How long the last chat is allowed to take letting go before this window says so out loud.</summary>
    private static readonly TimeSpan PackingUpGrace = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Whether another chat may be started now. Usually an instant yes. It is the second chat in a row that
    /// needs the patience: the machine cannot listen on the same service twice at once, so a radio that has
    /// not finished unregistering the last one would answer the new attempt with a failure that reads like a
    /// broken Bluetooth rather than like an overlap.
    /// </summary>
    private async Task<bool> LetsStartAnotherChatAsync()
    {
        var still = Interlocked.Exchange(ref packingUp, null);

        if (still is null || still.IsCompleted)
        {
            return true;
        }

        SetBusy(true);
        SetStatus("the last chat is still packing up…");

        try
        {
            await still.WaitAsync(PackingUpGrace);
            return true;
        }
        catch (TimeoutException)
        {
            SetStatus("the last chat has not finished letting go — try again in a moment");
            Resume();
            return false;
        }
        catch (Exception)
        {
            return true;        // it finished, and whatever it was complaining about was reported by the service
        }
    }

    /// <summary>
    /// The last chat's room list is owed to the file (F9), and after this window closes there is no message
    /// loop left to finish the work on. Bounded, because a stuck disk is a worse bug than a room list one
    /// change out of date — the same bargain <see cref="ChatServer.StopAsync"/> makes.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        if (e.Cancel)
        {
            return;
        }

        var still = Interlocked.Exchange(ref packingUp, null);

        if (still is { IsCompleted: false })
        {
            SetStatus("writing the room list…");

            try
            {
                still.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // Nothing is left to tell, and the process is going anyway.
            }
        }
    }

    /// <summary>
    /// Opening a chat window, and remembering the service it was built on: this window made that service, so
    /// this window is the one that puts it away once the chat window has stopped it (a joined chat left
    /// undisposed is an RFCOMM channel left open, and a radio only has a few of them).
    /// </summary>
    private void Open(ChatForm chat, IChatService service)
    {
        // F10 belongs to the composed program rather than to the window: a ChatForm on its own - in a test,
        // or in whatever gets built next - should not be making sounds that nobody asked for.
        chat.AttachSounds(new SoundNotifier());
        lastService = service;

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

    /// <summary>
    /// What the status bar says, which is where this window puts every answer it has for a person - including
    /// the ones that arrive from a radio that is not there. Readable from outside because a program that only
    /// explains itself in a bar needs a way to be asked what it just said.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string StatusText => lblStatus.Text ?? string.Empty;
}
