using System.Diagnostics;
using Lantern.Core;
using Lantern.Transport;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// The end of a chat window, and the end of the program are different things. Closing a chat window comes
/// back to the start screen — where a pairing that died, a host that was not switched on, or a nickname
/// typed wrong can be tried again without starting the program from the beginning — and it comes back
/// <i>promptly</i>, because hanging up a Bluetooth channel is the part that takes seconds and it is not a
/// reason for a window to appear frozen.
/// </summary>
public sealed class ClosingTheChatWindowTests
{
    // ------------------------------------------------------------ back to the start screen

    [Fact]
    public Task ClosingTheHostWindowBringsTheStartScreenBack() => Sta.RunAsync(async () =>
    {
        var radio = new Radio();

        using var form = new TellingStartup(radio);
        form.Show();

        Click(form, "btnStartServer");
        await Watch.UntilAsync(() => form.Opened is not null, "the chat window to be built");

        Assert.False(form.Visible, "the start screen gets out of the way while there is a chat");

        form.Opened!.Close();

        Assert.True(form.Visible, "and comes back when the chat is over, rather than taking the program with it");
        Assert.True(form.Hosting is null, "this machine is not hosting anything any more");
        Assert.Contains("the chat is closed", form.StatusText, StringComparison.Ordinal);
        Assert.True(Assert.IsType<Button>(Find(form, "btnStartServer")).Enabled, "the buttons are the person's again");
        Assert.True(radio.Server!.HasLetGo, "the chat really stopped, it was not merely hidden");

        // And the start screen is a working start screen, not a painting: hosting again works.
        Click(form, "btnStartServer");
        await Watch.UntilAsync(() => form.Hosting is not null, "a second chat on the same start screen");
        Assert.Equal(2, radio.ServersBuilt);
    });

    [Fact]
    public Task ClosingTheClientWindowBringsTheStartScreenBack() => Sta.RunAsync(async () =>
    {
        var radio = new Radio();

        using var form = new TellingStartup(radio);
        form.Show();

        await JoinAsync(form);

        Assert.IsAssignableFrom<ClientForm>(form.Opened);

        form.Opened!.Close();

        Assert.True(form.Visible);
        Assert.Contains("the chat is closed", form.StatusText, StringComparison.Ordinal);
        Assert.True(radio.Dialled!.IsClosed, "the channel back to the host is not left lying open");
    });

    [Fact]
    public Task TheStartScreenSaysWhatTheChatEndedWith() => Sta.RunAsync(async () =>
    {
        var radio = new Radio();

        using var form = new TellingStartup(radio);
        form.Show();

        await JoinAsync(form);
        var chat = Assert.IsType<TellingClient>(form.Opened);

        // Scenario 6 arriving from the other end: the window says why and steps aside. What it must not do
        // is arrive back here and say nothing — a start screen that appears without explanation looks like
        // a program that was never in a chat at all.
        chat.LostItsHost();

        await Watch.UntilAsync(() => !chat.Visible, "the chat window to step aside");

        Assert.True(form.Visible);
        Assert.Contains("host closed the connection", form.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.True(Assert.IsType<Button>(Find(form, "btnStartServer")).Enabled, "and it is a working start screen, not a paused one");
    });

    // ----------------------------------------------------------------- not standing still

    [Fact]
    public Task AWindowDoesNotStandWaitingForARadioThatIsSlowToLetGo() => Sta.RunAsync(async () =>
    {
        var radio = new Radio(TimeSpan.FromSeconds(3));

        await using var server = new ChatServer(radio.NewServer("Slow Host"), "Slow Host");
        await server.StartAsync();
        await using var host = new HostChatService(server, "Hosty");
        await host.StartAsync();

        using var form = new QuietHost(host);
        form.Show();

        var took = Stopwatch.StartNew();
        form.Close();
        took.Stop();

        // The window went. Three seconds of Bluetooth is not nothing, and it used to be spent with a
        // window already off the screen and the thread that owned it nowhere to be reached.
        Assert.True(took.Elapsed < TimeSpan.FromSeconds(2), $"closing the window took {took.ElapsedMilliseconds} ms");

        Assert.False(server.IsRunning, "it has agreed to stop");
        Assert.False(form.StoppingChat.IsCompleted, "...and is still doing it, which is somebody else's problem now");

        await form.StoppingChat;
        Assert.True(radio.Server!.HasLetGo, "the hang-up did finish, it was only not waited on");
    });

    [Fact]
    public Task StartingASecondChatWaitsForTheFirstOneToFinishLettingGo() => Sta.RunAsync(async () =>
    {
        var radio = new Radio(TimeSpan.FromSeconds(1.5));

        using var form = new TellingStartup(radio);
        form.Show();

        Click(form, "btnStartServer");
        await Watch.UntilAsync(() => form.Opened is not null, "the first chat");
        Assert.Equal(1, radio.ServersBuilt);

        form.Opened!.Close();

        // Straight back for another one, while the radio is still unregistering the last. A machine cannot
        // advertise the same service twice at once, so the honest answer is to say it is not ready yet:
        // starting anyway buys a failure that reads like broken Bluetooth rather than like an overlap.
        Click(form, "btnStartServer");

        Assert.Contains("packing up", form.StatusText, StringComparison.Ordinal);
        Assert.Equal(1, radio.ServersBuilt);

        await Watch.UntilAsync(() => form.Hosting is not null, "the second chat to start once the first had gone");

        Assert.Equal(2, radio.ServersBuilt);
        Assert.DoesNotContain("packing up", form.StatusText, StringComparison.Ordinal);
        Assert.False(form.Visible, "and it opened the window it was asked for");
    });

    [Fact]
    public Task ClosingTheStartScreenDoesNotWalkAwayFromAChatStillPackingUp() => Sta.RunAsync(async () =>
    {
        var radio = new Radio(TimeSpan.FromSeconds(1.5));

        using var form = new TellingStartup(radio);
        form.Show();

        Click(form, "btnStartServer");
        await Watch.UntilAsync(() => form.Opened is not null, "the chat window");

        form.Opened!.Close();
        Assert.False(radio.Server!.HasLetGo, "the radio has not finished with the last chat yet");

        // After this window there is no message loop left to finish the work in, and the room list is owed
        // to the file (F9) — so the program's ✕ is where waiting is correct, and it is bounded there rather
        // than at every window.
        form.Close();

        Assert.True(radio.Server.HasLetGo, "the program waited for the chat it was leaving");
    });

    // ---------------------------------------------------------------------- the furniture

    private static async Task JoinAsync(TellingStartup form)
    {
        Click(form, "btnScan");
        await Watch.UntilAsync(
            () => Status(form).Contains("1 chat(s) found", StringComparison.Ordinal),
            "the scan to list a chat");

        Assert.IsType<ListBox>(Find(form, "lstServers")).SelectedIndex = 0;
        Click(form, "btnJoin");
        await Watch.UntilAsync(() => form.Opened is not null, "the chat window to be built");
    }

    private static Control Find(Control owner, string name)
        => owner.Controls.Find(name, searchAllChildren: true).FirstOrDefault()
           ?? throw new InvalidOperationException($"the window has no control named '{name}'");

    private static void Click(Control owner, string name)
        => Assert.IsType<Button>(Find(owner, name)).PerformClick();

    private static string Status(Control form)
    {
        var strip = Assert.IsType<StatusStrip>(Find(form, "statusStrip"));
        return strip.Items.Cast<ToolStripItem>().Single(item => item.Name == "lblStatus").Text ?? string.Empty;
    }

    /// <summary>The start screen with the chat window it builds left within reach, and no dialogs of any kind.</summary>
    private sealed class TellingStartup(ITransportFactory transport) : StartupForm(transport)
    {
        /// <summary>The chat window this window built, once it has built one.</summary>
        public ChatForm? Opened { get; private set; }

        protected override ChatForm NewHostWindow(IChatService service) => Opened = new QuietHost(service);

        protected override ChatForm NewClientWindow(IChatService service) => Opened = new TellingClient(service);
    }

    /// <summary>A host window that does not ask, because a question nobody is there to answer is a test that never ends.</summary>
    private sealed class QuietHost(IChatService service) : HostForm(service)
    {
        protected override bool ConfirmQuit() => true;
    }

    /// <summary>A client window whose connection can be lost on purpose, and which does not sit in a modal about it.</summary>
    private sealed class TellingClient(IChatService service) : ClientForm(service)
    {
        public void LostItsHost() => OnConnectionLost(new IOException("The host closed the connection."));

        protected override void ShowConnectionLost(Exception error)
        {
            // The box is the one part of this path a test must not sit in.
        }
    }

    /// <summary>
    /// A radio, for a start screen to host on and join through. <paramref name="lettingGo"/> is how long it
    /// takes to stop advertising a chat, which on real hardware is the difference between a close that feels
    /// instant and one that feels broken; zero is what a loopback or a machine that is still switched on costs.
    /// </summary>
    private sealed class Radio(TimeSpan? lettingGo = null) : ITransportFactory
    {
        private readonly TimeSpan lettingGo = lettingGo ?? TimeSpan.Zero;

        public RadioServer? Server { get; private set; }

        public SilentChannel? Dialled { get; private set; }

        public int ServersBuilt { get; private set; }

        public string Name => "Bluetooth";

        public string Description => "Bluetooth between two laptops";

        public IBtServer NewServer(string serverName)
        {
            ServersBuilt++;
            return Server = new RadioServer(this);
        }

        public IBtClient NewClient() => new RadioClient(this);

        private void HangUp()
        {
            if (lettingGo > TimeSpan.Zero)
            {
                Thread.Sleep(lettingGo);
            }
        }

        public sealed class RadioServer(Radio owner) : IBtServer
        {
            private readonly TaskCompletionSource<IClientLink?> nobodyMore = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int letGo;
            private int listening = 1;

            public bool HasLetGo => Volatile.Read(ref letGo) == 1;

            public bool IsListening => Volatile.Read(ref listening) == 1;

            public ServerEndpoint Endpoint => new("Kitchen Table", "AC86A3294B28", LanternProtocol.ServiceId);

            public Task StartAsync(Guid serviceId) => Task.CompletedTask;

            public Task<IClientLink?> AcceptAsync(CancellationToken cancel = default)
            {
                // The real accept waits, and stopping the listener is what breaks the wait; nobody joins
                // this chat in these tests, so it waits until then.
                return nobodyMore.Task;
            }

            public void Stop()
            {
                if (Interlocked.Exchange(ref listening, 0) == 1)
                {
                    owner.HangUp();
                    Volatile.Write(ref letGo, 1);
                }

                nobodyMore.TrySetResult(null);
            }

            public ValueTask DisposeAsync()
            {
                Stop();
                return ValueTask.CompletedTask;
            }
        }

        private sealed class RadioClient(Radio owner) : IBtClient
        {
            public Task<IClientLink> ConnectAsync(ServerEndpoint endpoint, CancellationToken cancel = default)
                => Task.FromResult<IClientLink>(owner.Dialled = new SilentChannel(owner.HangUp));

            public Task<IReadOnlyList<ServerEndpoint>> DiscoverAsync(TimeSpan timeout, CancellationToken cancel = default)
                => Task.FromResult<IReadOnlyList<ServerEndpoint>>(
                    [new ServerEndpoint("Kitchen Table", "AC86A3294B28", LanternProtocol.ServiceId)]);
        }
    }

    /// <summary>
    /// A channel that answers the handshake and then has nothing to say, which is what a joined chat with a
    /// quiet room looks like from the client side. Reads wait rather than ending: a stream that reaches its
    /// end would tell the client the host had gone, and in these tests it has not — hanging up is what ends
    /// the wait, and hanging up takes as long as the radio was given.
    /// </summary>
    private sealed class SilentChannel : IClientLink
    {
        private readonly Action hangUp;

        private readonly CancellationTokenSource closing = new();
        private readonly Stream input;

        public SilentChannel(Action hangUp)
        {
            this.hangUp = hangUp;

            // Built here rather than in an initializer: the stream needs the token that hangs it up.
            input = new ReadsThenWaits(
                new MemoryStream(FrameCodec.Encode(Frame.HelloAck("Kitchen Table", "AC86A3294B28"))),
                closing.Token);
        }

        public Stream Input => input;

        public Stream Output { get; } = new MemoryStream();

        public string RemoteName => "Kitchen Table";

        public string ConnectionId => "silent-channel";

        public bool IsClosed { get; private set; }

        public event EventHandler<Exception?>? Closed;

        public void Close()
        {
            if (IsClosed)
            {
                return;
            }

            IsClosed = true;
            hangUp();
            closing.Cancel();
            Closed?.Invoke(this, null);
        }

        public void Dispose() => Close();

        /// <summary>The bytes of the handshake first, and then the silence of a room nobody is talking in.</summary>
        /// <param name="closing">cancelled when the channel is hung up, which is what ends a waiting read</param>
        private sealed class ReadsThenWaits(Stream first, CancellationToken closing) : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => first.Length;

            public override long Position
            {
                get => first.Position;
                set => first.Position = value;
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
            {
                if (first.Position < first.Length)
                {
                    return await first.ReadAsync(buffer, cancel);
                }

                try
                {
                    await Task.Delay(Timeout.Infinite, closing);
                }
                catch (OperationCanceledException)
                {
                    // What a host hanging up reads as, from this side.
                    throw new IOException("The channel closed.");
                }

                return 0;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (first.Position < first.Length)
                {
                    return first.Read(buffer, offset, count);
                }

                closing.WaitHandle.WaitOne();
                throw new IOException("The channel closed.");
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
