using Lantern.Core;
using Lantern.Storage;
using Lantern.Transport;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T4.4: scenario 2 in clicks. Bob types a nickname, presses Scan, picks Alice and presses Join.
/// <para>
/// The transport is a fake, which is the point: a scan is a radio inquiry that takes seconds and may
/// never come back, and a suite cannot wait for hardware or pair laptops. What is under test is the
/// window's behaviour while that is happening — what is greyed out, what the button calls itself, what
/// the status bar says when nothing answers, and what gets remembered when something does.
/// </para>
/// </summary>
public class StartupScanUiTests
{
    [Fact]
    public void TheTransportIsStatedBeforeAnythingIsClicked()
    {
        Sta.Run(() =>
        {
            using var form = new StartupForm(new FakeTransport(when: "over the fake transport"));

            // A machine on loopback will never find a chat. If the window does not say so, the first
            // thing a person does is press Scan and wonder.
            Assert.Contains("over the fake transport", Status(form), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AScanFillsTheListAndSaysHowMany()
    {
        Sta.Run(() =>
        {
            var first = new ServerEndpoint("Kitchen Table", "AC86A3294B28", LanternProtocol.ServiceId);
            var second = new ServerEndpoint("Garden", "34885DBDCC01", LanternProtocol.ServiceId);
            var transport = new FakeTransport(when: "over Bluetooth");
            transport.Scan = _ => Task.FromResult<IReadOnlyList<ServerEndpoint>>(new[] { first, second });

            using var form = new StartupForm(transport);
            form.Show();

            Click(form, "btnScan");

            PumpUntil(() => Status(form).Contains("2 chat(s) found", StringComparison.Ordinal), "the scan to be reported");

            var list = Assert.IsType<ListBox>(Find(form, "lstServers"));
            Assert.Equal(
                new[] { "Kitchen Table", "Garden" },
                list.Items.Cast<ServerEndpoint>().Select(e => e.ServerName));

            // And the reason for the whole list: one row, selected, becomes a Join that works.
            list.SelectedIndex = 0;
            Assert.True(Assert.IsType<Button>(Find(form, "btnJoin")).Enabled);
            Assert.True(Assert.IsType<Button>(Find(form, "btnScan")).Enabled, "the window is usable again");
        });
    }

    [Fact]
    public void AScanSaysWhatToCheckWhenNothingAnswers()
    {
        Sta.Run(() =>
        {
            var transport = new FakeTransport(when: "over Bluetooth");
            transport.Scan = _ => Task.FromResult<IReadOnlyList<ServerEndpoint>>([]);

            using var form = new StartupForm(transport);
            form.Show();

            Click(form, "btnScan");
            PumpUntil(() => Status(form).Contains("no Lantern chat found", StringComparison.Ordinal), "the empty scan to be reported");

            // "Nothing found" is not actionable. The two reasons it is usually true are, and they are in
            // the sentence (docs/testing/bluetooth-pairing-how-to.md).
            Assert.Contains("paired", Status(form), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Assert.IsType<ListBox>(Find(form, "lstServers")).Items);
        });
    }

    [Fact]
    public void TheButtonThatStartedAScanIsTheOneThatStopsIt()
    {
        Sta.Run(() =>
        {
            var transport = new FakeTransport(when: "over Bluetooth");

            // An inquiry that never returns is the normal case for a friend whose machine is switched
            // off, and there is no Stop button in the figure to wait for.
            transport.Scan = async cancel =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancel);
                return [];      // never reached: the wait only ends when the person cancels
            };

            using var form = new StartupForm(transport);
            form.Show();

            Click(form, "btnScan");

            var scan = Assert.IsType<Button>(Find(form, "btnScan"));
            Assert.Equal("Scanning…", scan.Text);       // busy, and saying so
            Assert.True(scan.Enabled, "a scan in flight has to be clickable, because it is the cancel");
            Assert.False(Assert.IsType<Button>(Find(form, "btnStartServer")).Enabled, "and nothing else is");

            Click(form, "btnScan");

            PumpUntil(() => Status(form).Contains("scan stopped", StringComparison.Ordinal), "the cancelled scan to be reported");
            Assert.Equal("Scan", scan.Text);
            Assert.True(Assert.IsType<Button>(Find(form, "btnStartServer")).Enabled);
        });
    }

    [Fact]
    public void AChatThatAnsweredIsRememberedForNextTime()
    {
        Sta.Run(() =>
        {
            var host = new ServerEndpoint("Kitchen Table", "AC86A3294B28", LanternProtocol.ServiceId);
            var transport = new FakeTransport(when: "over Bluetooth");
            transport.Scan = _ => Task.FromResult<IReadOnlyList<ServerEndpoint>>([host]);
            transport.Dial = _ => Task.FromResult<IClientLink>(new AnsweringLink());

            var known = new RecordingKnownHosts();

            using var form = new WatchingStartup(transport, known);
            form.Show();

            Click(form, "btnScan");
            PumpUntil(() => Status(form).Contains("1 chat(s) found", StringComparison.Ordinal), "the scan");

            var list = Assert.IsType<ListBox>(Find(form, "lstServers"));
            list.SelectedIndex = 0;
            Click(form, "btnJoin");

            PumpUntil(() => form.Opened is not null, "the chat window to be built");

            Assert.False(form.Visible, "the startup window gets out of the way");
            Assert.IsType<ClientForm>(form.Opened);
            Assert.Equal(new[] { host.DeviceAddress }, known.Remembered.Select(e => e.DeviceAddress));
        });
    }

    [Fact]
    public void AChatThatDidNotAnswerLeavesTheWindowAsItWas()
    {
        Sta.Run(() =>
        {
            var host = new ServerEndpoint("Kitchen Table", "AC86A3294B28", LanternProtocol.ServiceId);
            var transport = new FakeTransport(when: "over Bluetooth");
            transport.Scan = _ => Task.FromResult<IReadOnlyList<ServerEndpoint>>([host]);
            transport.Dial = _ => Task.FromException<IClientLink>(new IOException("AC86A3294B28 did not answer."));

            var known = new RecordingKnownHosts();

            using var form = new WatchingStartup(transport, known);
            form.Show();

            Click(form, "btnScan");
            PumpUntil(() => Status(form).Contains("1 chat(s) found", StringComparison.Ordinal), "the scan");

            Assert.IsType<ListBox>(Find(form, "lstServers")).SelectedIndex = 0;
            Click(form, "btnJoin");

            PumpUntil(() => Status(form).Contains("did not answer", StringComparison.Ordinal), "the failed join to be reported");

            // A machine that was not there is not a machine this laptop joined, and the next join list
            // is short enough without lies in it.
            Assert.Empty(known.Remembered);
            Assert.True(Assert.IsType<Button>(Find(form, "btnJoin")).Enabled, "so try the row again, or another one");
            Assert.Null(form.Opened);
            Assert.True(form.Visible, "and this window is still the one in front of them");
        });
    }

    [Fact]
    public void TheWindowActsOnTheTransportItWasGivenAndOnNoOther()
    {
        Sta.Run(() =>
        {
            var transport = new FakeTransport(when: "over Bluetooth");
            transport.Accept = _ => Task.FromResult<IClientLink?>(null);       // nobody joins this one

            using var form = new WatchingStartup(transport);
            form.Show();

            Click(form, "btnStartServer");

            PumpUntil(() => form.Hosting is not null, "this window to be hosting");

            Assert.Equal(Environment.MachineName, transport.Servers[0]);   // not a transport it was not given
            Assert.IsType<HostForm>(form.Opened);
            Assert.False(form.Visible);
        });
    }

    // ------------------------------------------------------------------ plumbing

    private static Control? Find(Control parent, string name)
        => parent.Controls.Find(name, searchAllChildren: true).FirstOrDefault();

    private static void Click(Form form, string name) => Assert.IsType<Button>(Find(form, name)).PerformClick();

    private static string Status(Control parent)
    {
        var strip = parent.Controls.OfType<StatusStrip>().Single();
        return strip.Items.Cast<ToolStripItem>().Single(item => item.Name == "lblStatus").Text ?? string.Empty;
    }

    /// <summary>
    /// Pumps the message loop while waiting. An <c>async void</c> click handler resumes on this thread's
    /// WinForms synchronization context, which means its continuations only run when the loop is pumped;
    /// blocking on a task here would hang the very thread the handler needs.
    /// </summary>
    private static void PumpUntil(Func<bool> condition, string because, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            Application.DoEvents();
            Thread.Sleep(5);
        }

        throw new TimeoutException($"timed out after {timeoutMs} ms waiting for {because}");
    }

    /// <summary>
    /// The same window, keeping hold of the chat window it was told to step aside for. The alternative is
    /// hunting <c>Application.OpenForms</c>, which is one list shared by every test class running in
    /// parallel, and which would then have to be closed - a chat window asks before it closes.
    /// </summary>
    private sealed class WatchingStartup : StartupForm
    {
        public WatchingStartup(ITransportFactory transport, IKnownHosts? knownHosts = null)
            : base(transport, knownHosts)
        {
        }

        /// <summary>The chat window this window built, once it has built one.</summary>
        public ChatForm? Opened { get; private set; }

        protected override void ShowChat(ChatForm chat)
        {
            Opened = chat;
            Hide();     // deliberately not shown: building it and stepping aside is the part under test
        }
    }

    /// <summary>A transport that never touches a radio, and remembers what it was asked to do.</summary>
    private sealed class FakeTransport(string when) : ITransportFactory
    {
        public Func<CancellationToken, Task<IReadOnlyList<ServerEndpoint>>>? Scan { get; set; }

        public Func<ServerEndpoint, Task<IClientLink>>? Dial { get; set; }

        public Func<CancellationToken, Task<IClientLink?>>? Accept { get; set; }

        public List<string> Servers { get; } = [];

        public string Name => when.Contains("Bluetooth") ? "Bluetooth" : "test";

        public string Description => when;

        public IBtServer NewServer(string serverName)
        {
            Servers.Add(serverName);
            return new FakeServer(Accept);
        }

        public IBtClient NewClient() => new FakeClient(this);

        private sealed class FakeClient(FakeTransport owner) : IBtClient
        {
            public Task<IClientLink> ConnectAsync(ServerEndpoint endpoint, CancellationToken cancel = default)
                => owner.Dial is { } dial
                    ? dial(endpoint)
                    : Task.FromException<IClientLink>(new NotSupportedException("this fake has nothing to join"));

            public Task<IReadOnlyList<ServerEndpoint>> DiscoverAsync(TimeSpan timeout, CancellationToken cancel = default)
                => owner.Scan is { } scan
                    ? scan(cancel)
                    : Task.FromResult<IReadOnlyList<ServerEndpoint>>([]);
        }

        private sealed class FakeServer(Func<CancellationToken, Task<IClientLink?>>? accept) : IBtServer
        {
            private bool listening;

            public Task StartAsync(Guid serviceId)
            {
                listening = true;
                return Task.CompletedTask;
            }

            public Task<IClientLink?> AcceptAsync(CancellationToken cancel = default)
                => accept is { } a
                    ? a(cancel)
                    : Task.FromResult<IClientLink?>(null);

            public void Stop() => listening = false;

            public bool IsListening => listening;

            public ServerEndpoint Endpoint => new("fake", "FAKE", LanternProtocol.ServiceId);

            public ValueTask DisposeAsync()
            {
                listening = false;
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>
    /// A link that answers the Lantern handshake and then goes quiet, which is all a Join needs in order
    /// to have happened.
    /// </summary>
    private sealed class AnsweringLink : IClientLink
    {
        public AnsweringLink()
            => Input = new MemoryStream(FrameCodec.Encode(Frame.HelloAck("Haitao", "Kitchen Table")));

        public Stream Input { get; }

        public Stream Output { get; } = new MemoryStream();

        public string RemoteName => "Kitchen Table";

        public string ConnectionId => "answer-link";

        public event EventHandler<Exception?>? Closed;

        public void Close() => Closed?.Invoke(this, null);

        public void Dispose() => Close();
    }

    /// <summary>An <see cref="IKnownHosts"/> that only remembers, because that is the half being tested.</summary>
    private sealed class RecordingKnownHosts : IKnownHosts
    {
        public List<ServerEndpoint> Remembered { get; } = [];

        public event EventHandler<string>? SaveFailed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<ServerEndpoint> Recent() => Remembered;

        public void Remember(ServerEndpoint endpoint) => Remembered.Add(endpoint);

        public Task SavesCompletedAsync() => Task.CompletedTask;
    }
}
