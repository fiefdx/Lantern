using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Lantern.Core;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T4.2: the RFCOMM transport, tested down to the seam.
/// <para>
/// A test suite cannot pair laptops, and this project should not pretend otherwise. What can be tested
/// is everything Lantern decides for itself — what an address is allowed to look like, who the accept
/// loop tells about, what happens to open links when the host quits, which machines a scan may list, and
/// what a failure is called in a status bar. The three classes that actually speak to 32feet.NET are
/// exercised by hand with <c>tools/BtSpike</c>, on hardware, in
/// <c>docs/testing/bluetooth-pairing-how-to.md</c>.
/// </para>
/// </summary>
public sealed class RfcommTransportTests
{
    // ------------------------------------------------------------- addresses

    [Theory]
    [InlineData("F8E4E3F9603B")]
    [InlineData("f8e4e3f9603b")]
    [InlineData("  F8E4E3F9603B  ")]
    [InlineData("F8-E4-E3-F9-60-3B")]
    [InlineData("f8:e4:e3:f9:60:3b")]
    [InlineData("F8 E4 E3 F9 60 3B")]
    public void AnAddressIsAcceptedInAnyShapeAPersonMightWriteIt(string text)
    {
        Assert.True(RfcommAddress.TryParse(text, out var address));
        Assert.Equal("F8E4E3F9603B", RfcommAddress.Format(address));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("F8E4E3F9603")]          // one digit short
    [InlineData("F8E4E3F9603B1")]        // and one long
    [InlineData("F8E4E3F9603G")]         // not hexadecimal
    [InlineData("F8E4E3F9603BZZ")]       // hexadecimal, but not address-shaped: not quietly trimmed
    [InlineData("F8E4E3-F9603B")]        // partly grouped
    [InlineData("F8e4 E3-f9:603b")]      // three separator styles and four groups: not cleaned up into something dialable
    [InlineData("127.0.0.1:43797")]      // a loopback address, which is a different transport's business
    [InlineData("localhost")]
    [InlineData("haitao-iPhone")]
    public void AnythingElseIsRefusedRatherThanGuessedAt(string? text)
    {
        Assert.False(RfcommAddress.TryParse(text, out _));
    }

    [Fact]
    public void ARefusedAddressSaysWhatOneLooksLike()
    {
        var ex = Assert.Throws<FormatException>(() => RfcommAddress.Parse("kitchen"));
        Assert.Contains("kitchen", ex.Message, StringComparison.Ordinal);
        Assert.Contains("F8E4E3F9603B", ex.Message, StringComparison.Ordinal);
    }

    // --------------------------------------------------------------- the link

    [Fact]
    public void ClosingALinkReleasesTheChannelThatMadeItAndSaysSoOnce()
    {
        var channel = new FakeConnection();
        var link = new StreamLink(channel.Stream, remoteName: "haitao-iPhone", owner: channel);

        var closed = 0;
        link.Closed += (_, _) => closed++;

        link.Close();
        link.Close();

        Assert.Equal(1, closed);
        Assert.True(channel.IsClosed);       // the radio handle must not outlive the link
    }

    [Fact]
    public void ALinkWithoutANameFromTheStackIsStillALink()
    {
        using var link = new StreamLink(new MemoryStream(), remoteName: "   ");

        Assert.Equal("unknown", link.RemoteName);
        Assert.NotEmpty(link.ConnectionId);       // the server keys its sessions on it
        Assert.Same(link.Input, link.Output);     // one duplex stream, read and written from two tasks
    }

    // --------------------------------------------------------------- the host

    [Fact]
    public void AServerThatIsNotListeningHasNoAddressToAdvertise()
    {
        var server = new BtRfcommServer("Test Host", new FakeListener());

        Assert.False(server.IsListening);
        Assert.Throws<InvalidOperationException>(() => server.Endpoint);
    }

    [Fact]
    public async Task HostingPublishesTheLanternServiceAndTellsNobodyElse()
    {
        var listener = new FakeListener();
        await using var server = new BtRfcommServer("Test Host", listener, localAddress: () => "F8E4E3F9603B");

        await server.StartAsync(LanternProtocol.ServiceId);

        Assert.Equal(LanternProtocol.ServiceId, listener.StartedWithService);
        Assert.Equal("Lantern", listener.StartedWithServiceName);   // T4.1 D4: a name is the only thing a person can read
        Assert.True(server.IsListening);
        Assert.Equal("F8E4E3F9603B", server.Endpoint.DeviceAddress);
        Assert.Equal("Test Host", server.Endpoint.ServerName);

        // Starting twice is what happens when a window is reopened, and it must not double-register.
        listener.StartCount = 0;
        await server.StartAsync(LanternProtocol.ServiceId);
        Assert.Equal(0, listener.StartCount);
    }

    [Fact]
    public async Task AHostThatCannotRegisterTheServiceSaysWhatToCheck()
    {
        var server = new BtRfcommServer("Test Host", new FakeListener(startThrows: new IOException("only one radio and it is busy")));

        var ex = await Assert.ThrowsAsync<IOException>(() => server.StartAsync(LanternProtocol.ServiceId));

        // "only one radio and it is busy" is true and useless on its own; the two things it can actually
        // be are named, because this is the first failure a field test meets.
        Assert.Contains("hosting", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("radio", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(server.IsListening);
    }

    [Fact]
    public async Task AnAcceptedChannelBecomesALinkTheServerKnowsAbout()
    {
        var listener = new FakeListener();
        await using var server = new BtRfcommServer("Test Host", listener);
        await server.StartAsync(LanternProtocol.ServiceId);

        listener.Arriving(new FakeConnection("haitao-iPhone"));
        var link = await server.AcceptAsync();

        Assert.NotNull(link);
        Assert.Equal("haitao-iPhone", link.RemoteName);
        Assert.Single(server.Links);

        await link.Output.WriteAsync(new byte[] { 1, 2, 3 });

        // Bytes the session wrote are the bytes the far end receives: the frame codec is meant to be
        // unable to tell this from TCP, and it cannot.
        Assert.Equal(new byte[] { 1, 2, 3 }, listener.LastConnection!.Received);
    }

    [Fact]
    public async Task QuittingTheHostDropsEveryClientAndUntiesTheService()
    {
        var listener = new FakeListener();
        var server = new BtRfcommServer("Test Host", listener);
        await server.StartAsync(LanternProtocol.ServiceId);

        listener.Arriving(new FakeConnection("one"));
        listener.Arriving(new FakeConnection("two"));
        var first = await server.AcceptAsync();
        var second = await server.AcceptAsync();
        Assert.NotNull(first);
        Assert.NotNull(second);

        var dropped = 0;
        first!.Closed += (_, _) => dropped++;
        second!.Closed += (_, _) => dropped++;

        server.Stop();

        Assert.Equal(2, dropped);                                   // scenario 6 starts here
        Assert.Empty(server.Links);
        Assert.True(listener.IsStopped);                            // Windows must stop advertising it
        Assert.True(listener.LastConnection!.IsClosed);
        Assert.Null(await server.AcceptAsync());                    // and nothing further is accepted
        server.Stop();                                              // twice is fine
    }

    /// <summary>
    /// The part of quitting a Bluetooth host that takes time is the saying-goodbye: the stack wants a polite
    /// close from a machine that may be in a bag, and it will spend seconds on it. <see cref="BtRfcommServer"/>
    /// starts every goodbye at once and waits a second, no longer — the close is not cancelled by walking away,
    /// only no longer stood still for.
    /// </summary>
    [Fact]
    public async Task StoppingARadioThatIsSlowToLetGoDoesNotHoldTheHostUp()
    {
        var goodbye = TimeSpan.FromSeconds(3);
        var listener = new SlowGoodbyeListener(goodbye);
        await using var server = new BtRfcommServer("Slow Host", listener);
        await server.StartAsync(LanternProtocol.ServiceId);

        var first = await server.AcceptAsync();
        var second = await server.AcceptAsync();
        Assert.NotNull(first);
        Assert.NotNull(second);

        var took = Stopwatch.StartNew();
        server.Stop();
        took.Stop();

        Assert.False(server.IsListening);
        Assert.Empty(server.Links);
        Assert.True(
            took.Elapsed < BtRfcommServer.TearDownGrace + TimeSpan.FromSeconds(1),
            $"stopping waited {took.ElapsedMilliseconds} ms on a radio that wanted three seconds apiece");

        // The other half: the goodbyes that were not waited on are still happening, and they finish. A close
        // that gave up instead of giving up waiting would leave the service registered and the next visitor
        // dialling nothing.
        await listener.HasStopped;
        await listener.EveryChannelClosed;
        Assert.True(listener.EveryChannel.Every, "a channel left open is an SDP record and a radio handle left behind");
        Assert.Equal(0, listener.EveryChannel.StillOpen);
    }

    [Fact]
    public async Task ARadioThatRefusesToAcceptEndsTheLoopRatherThanFaultingIt()
    {
        var listener = new FakeListener { AcceptThrows = new IOException("the radio went out") };
        await using var server = new BtRfcommServer("Test Host", listener);
        await server.StartAsync(LanternProtocol.ServiceId);

        Assert.Null(await server.AcceptAsync());
    }

    [Fact]
    public async Task AChannelThatArrivesWhileStoppingIsClosedRatherThanHandedUp()
    {
        var listener = new FakeListener();
        var server = new BtRfcommServer("Test Host", listener);
        await server.StartAsync(LanternProtocol.ServiceId);

        var late = new FakeConnection("late");
        listener.Arriving(late);
        server.Stop();

        Assert.Null(await server.AcceptAsync());
        Assert.True(late.IsClosed);       // a channel that arrives into a shutting-down host is not handed to anybody
    }

    // ------------------------------------------------------------- the client

    [Fact]
    public async Task JoiningAMachineThatIsNotThereIsToldAsAHumanProblem()
    {
        var connector = new FakeConnector(_ => throw new SocketException((int)SocketError.TimedOut));
        var client = new BtRfcommClient(connector);

        var ex = await Assert.ThrowsAsync<IOException>(
            () => client.ConnectAsync(new ServerEndpoint("haitao-iPhone", "AC86A3294B28", LanternProtocol.ServiceId)));

        // The words matter: this is what the window shows, and the person reading it has to be able to
        // act on it (docs/testing/bluetooth-pairing-how-to.md).
        Assert.Contains("switched on", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AC86A3294B28", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SocketError.TimedOut, "did not answer")]
    [InlineData(SocketError.AddressNotAvailable, "cannot dial")]
    [InlineData(SocketError.ConnectionRefused, "not hosting")]
    [InlineData(SocketError.HostNotFound, "Pair")]
    public async Task EachWayAConnectFailsGetsItsOwnSentence(SocketError code, string expected)
    {
        var connector = new FakeConnector(_ => throw new SocketException((int)code));
        var client = new BtRfcommClient(connector);

        var ex = await Assert.ThrowsAsync<IOException>(
            () => client.ConnectAsync(new ServerEndpoint("x", "AC86A3294B28", LanternProtocol.ServiceId)));

        // Each of these needs a different thing to be done about it, which is why they are not one
        // message. (docs/testing/bluetooth-pairing-how-to.md)
        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AJoinAttemptThatGoesNowhereIsStoppedRatherThanWaitedOutForever()
    {
        // A connect that never returns is the normal case for a machine that is switched off: the stack
        // gives up after about five seconds (T4.1), and Lantern has to stop waiting on its own schedule
        // rather than on the stack's.
        var started = false;
        var connector = new FakeConnector(async cancel =>
        {
            started = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancel);
            return null!;     // never reached: the wait only ends when it is cancelled
        });

        var client = new BtRfcommClient(connector, connectTimeout: TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<TimeoutException>(
            () => client.ConnectAsync(new ServerEndpoint("silent", "AC86A3294B28", LanternProtocol.ServiceId)));

        Assert.True(started);
    }

    [Fact]
    public async Task ACancelledJoinStaysACancellationAndDoesNotBlameTheOtherMachine()
    {
        var connector = new FakeConnector(async cancel =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancel);
            return null!;
        });
        using var user = new CancellationTokenSource();
        var client = new BtRfcommClient(connector, connectTimeout: TimeSpan.FromSeconds(30));

        var attempted = client.ConnectAsync(new ServerEndpoint("silent", "AC86A3294B28", LanternProtocol.ServiceId), user.Token);
        user.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempted);
        Assert.IsNotType<TimeoutException>(ex);       // "nobody answered" would send the user to check the other laptop
    }

    [Fact]
    public async Task AJoinIsDialledAtTheAddressGivenWithTheLanternService()
    {
        var connector = new FakeConnector(_ => Task.FromResult<IRfcommConnection>(new FakeConnection("Test Host")));
        var client = new BtRfcommClient(connector);

        var link = await client.ConnectAsync(new ServerEndpoint("Test Host", "f8-e4-e3-f9-60-3b", LanternProtocol.ServiceId));

        Assert.Equal("F8E4E3F9603B", connector.DialledAddress);   // normalised, whatever shape it arrived in
        Assert.Equal(LanternProtocol.ServiceId, connector.DialledService);
        Assert.Equal("Test Host", link.RemoteName);
    }

    [Fact]
    public async Task AnAddressThatIsNotOneIsRefusedBeforeAnythingIsDialled()
    {
        var connector = new FakeConnector(_ => Task.FromResult<IRfcommConnection>(new FakeConnection()));
        var client = new BtRfcommClient(connector);

        await Assert.ThrowsAsync<FormatException>(
            () => client.ConnectAsync(new ServerEndpoint("somewhere", "kitchen table", LanternProtocol.ServiceId)));

        Assert.Null(connector.DialledAddress);
    }

    // ------------------------------------------------------------- discovery

    [Fact]
    public async Task AScanListsWhatIsPairedPlusWhateverAnswers()
    {
        var finder = new FakeFinder("F8E4E3F9603B");
        finder.RememberedDevices.Add(new RfcommDevice("AC86A3294B28", "haitao-iPhone", true));
        finder.NearbyDevices.Add(new RfcommDevice("34885DBDCC01", "Keyboard K480", false));

        var found = await new BtRfcommClient(finder: finder).DiscoverAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { "haitao-iPhone", "Keyboard K480" }, found.Select(e => e.ServerName));
        Assert.All(found, endpoint => Assert.Equal(LanternProtocol.ServiceId, endpoint.ServiceId));
        Assert.Equal("AC86A3294B28", found[0].DeviceAddress);
        Assert.True(finder.InquiryWasAsked);       // and it was asked, whatever the paired list looked like
    }

    [Fact]
    public async Task AScanNeverListsTheMachineDoingTheLooking()
    {
        var finder = new FakeFinder("F8E4E3F9603B");
        finder.NearbyDevices.Add(new RfcommDevice("f8e4e3f9603b", "this machine", false));
        finder.RememberedDevices.Add(new RfcommDevice("F8E4E3F9603B", "this machine", true));

        var found = await new BtRfcommClient(finder: finder).DiscoverAsync(TimeSpan.FromSeconds(1));

        // Windows reports a machine's own adapter among what it can see, and a laptop cannot join its
        // own chat (T4.1).
        Assert.Empty(found);
    }

    [Fact]
    public async Task AMachineSeenTwiceIsListedOnceUnderTheNameItWasFirstHeardBy()
    {
        var finder = new FakeFinder(null);
        finder.RememberedDevices.Add(new RfcommDevice("AC86A3294B28", "haitao-iPhone", true));
        finder.NearbyDevices.Add(new RfcommDevice("ac:86:a3:29:4b:28", "iPhone of Haitao", false));

        var found = await new BtRfcommClient(finder: finder).DiscoverAsync(TimeSpan.FromSeconds(1));

        Assert.Single(found);
        Assert.Equal("haitao-iPhone", found[0].ServerName);
    }

    [Fact]
    public async Task ANameTheStackWillNotGiveStillLeavesAUsableLine()
    {
        var finder = new FakeFinder(null);
        finder.NearbyDevices.Add(new RfcommDevice("34885DBDCC01", "", false));

        var found = await new BtRfcommClient(finder: finder).DiscoverAsync(TimeSpan.FromSeconds(1));

        // A blank in a list of servers is worse than an address: it is the only thing the user can click.
        Assert.Equal("34885DBDCC01", Assert.Single(found).ServerName);
    }

    [Fact]
    public async Task AScanThatIsStoppedKeepsWhatAnswered()
    {
        var finder = new FakeFinder(null);
        finder.NearbyDevices.Add(new RfcommDevice("34885DBDCC01", "Keyboard K480", false));
        finder.StopAfterFirstNearby = true;

        var found = await new BtRfcommClient(finder: finder).DiscoverAsync(TimeSpan.FromSeconds(30));

        // Throwing away the one machine that showed up because the user stopped waiting is the opposite
        // of what a Stop button is for.
        Assert.Single(found);
    }

    [Fact]
    public async Task ARadioThatWillNotInquireStillLeavesThePairedList()
    {
        var finder = new FakeFinder(null);
        finder.RememberedDevices.Add(new RfcommDevice("AC86A3294B28", "haitao-iPhone", true));
        finder.InquiryThrows = new IOException("no radio present");

        var found = await new BtRfcommClient(finder: finder).DiscoverAsync(TimeSpan.FromSeconds(1));

        // A machine with no radio at all is a machine that can still be told what to do about it.
        Assert.Single(found);
    }

    [Fact]
    public async Task AStackThatRefusesToSayAnythingIsAnEmptyScanNotACrash()
    {
        var finder = new FakeFinder(null) { RememberedThrows = new InvalidOperationException("the stack said no") };

        Assert.Empty(await new BtRfcommClient(finder: finder).DiscoverAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ARoomFullOfLaptopsIsNotAList()
    {
        var finder = new FakeFinder(null);

        for (var i = 0; i < BtRfcommClient.MaxResults + 20; i++)
        {
            finder.NearbyDevices.Add(new RfcommDevice(i.ToString("X12"), $"laptop {i}", false));
        }

        var found = await new BtRfcommClient(finder: finder).DiscoverAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(BtRfcommClient.MaxResults, found.Count);
    }

    [Fact]
    public async Task GarbageFromTheStackIsLeftOutRatherThanShownAsAHOST()
    {
        var finder = new FakeFinder(null);
        finder.NearbyDevices.Add(new RfcommDevice("", "no address", false));
        finder.NearbyDevices.Add(new RfcommDevice("kitchen table", "misread", false));
        finder.NearbyDevices.Add(new RfcommDevice("34885DBDCC01", "Keyboard K480", false));

        var found = await new BtRfcommClient(finder: finder).DiscoverAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { "Keyboard K480" }, found.Select(e => e.ServerName));
    }

    // ------------------------------------------------------------------ fakes

    /// <summary>The radio, as a queue of things arriving.</summary>
    private sealed class FakeListener : IRfcommListener
    {
        private readonly Queue<FakeConnection> arriving = new();
        private readonly object gate = new();

        public FakeListener(Exception? startThrows = null) => StartThrows = startThrows;

        public Exception? StartThrows { get; set; }

        public Exception? AcceptThrows { get; set; }

        public Guid? StartedWithService { get; private set; }

        public string? StartedWithServiceName { get; private set; }

        public int StartCount { get; set; }

        public bool IsStopped { get; private set; }

        public FakeConnection? LastConnection { get; private set; }

        public bool IsActive { get; private set; }

        public void Start(Guid serviceId, string serviceName)
        {
            if (StartThrows is { } failure)
            {
                throw failure;
            }

            StartedWithService = serviceId;
            StartedWithServiceName = serviceName;
            StartCount++;
            IsActive = true;
        }

        public void Arriving(FakeConnection connection)
        {
            lock (gate)
            {
                arriving.Enqueue(connection);
            }
        }

        public Task<IRfcommConnection?> AcceptAsync(CancellationToken cancel)
        {
            if (AcceptThrows is { } failure)
            {
                return Task.FromException<IRfcommConnection?>(failure);
            }

            if (!IsActive)
            {
                return Task.FromResult<IRfcommConnection?>(null);
            }

            FakeConnection? next;

            lock (gate)
            {
                next = arriving.Count == 0 ? null : arriving.Dequeue();
            }

            if (next is null)
            {
                // The real accept waits; stopping the server is what breaks the wait, so stopping is
                // what this does too.
                return Task.FromResult<IRfcommConnection?>(null);
            }

            LastConnection = next;
            return Task.FromResult<IRfcommConnection?>(next);
        }

        public void Stop()
        {
            IsStopped = true;
            IsActive = false;

            FakeConnection[] pending;

            lock (gate)
            {
                pending = arriving.ToArray();
                arriving.Clear();
            }

            foreach (var connection in pending)
            {
                connection.Dispose();
            }
        }

        public void Dispose() => Stop();
    }

    /// <summary>
    /// A channel whose far end is in this process: writes are kept so a test can see what the session
    /// put on the wire, and reads return nothing, which is what a silent peer looks like.
    /// </summary>
    private sealed class FakeConnection(string remoteName = "somewhere") : IRfcommConnection
    {
        private readonly MemoryStream stream = new();

        public string RemoteName { get; } = remoteName;

        public bool IsClosed { get; private set; }

        public Stream Stream => stream;

        public byte[] Received => stream.ToArray();

        public void Close() => Dispose();

        public void Dispose() => IsClosed = true;
    }

    /// <summary>
    /// A radio that wants <paramref name="goodbye"/> to stop listening and the same again for every channel it
    /// is holding — the way a real one behaves when the machine on the other end has been switched off.
    /// </summary>
    private sealed class SlowGoodbyeListener(TimeSpan goodbye) : IRfcommListener
    {
        private readonly List<SlowGoodbyeConnection> channels = [];
        private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource everyChannelClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int open;

        public bool IsActive { get; private set; } = true;

        public Task HasStopped => stopped.Task;

        public Task EveryChannelClosed => everyChannelClosed.Task;

        public void Start(Guid serviceId, string serviceName)
        {
            IsActive = true;
        }

        public Task<IRfcommConnection?> AcceptAsync(CancellationToken cancel)
        {
            if (!IsActive)
            {
                return Task.FromResult<IRfcommConnection?>(null);
            }

            var channel = new SlowGoodbyeConnection(goodbye, Closed);

            lock (channels)
            {
                channels.Add(channel);
            }

            Interlocked.Increment(ref open);
            return Task.FromResult<IRfcommConnection?>(channel);
        }

        public void Stop()
        {
            IsActive = false;
            Thread.Sleep(goodbye);
            stopped.TrySetResult();
        }

        public void Dispose()
        {
            // Stopping is the disposal, and stopping has a test of its own.
        }

        private void Closed()
        {
            if (Interlocked.Decrement(ref open) == 0)
            {
                everyChannelClosed.TrySetResult();
            }
        }

        public (bool Every, int StillOpen) EveryChannel
        {
            get
            {
                SlowGoodbyeConnection[] snapshot;

                lock (channels)
                {
                    snapshot = channels.ToArray();
                }

                return (snapshot.All(c => c.IsClosed), Volatile.Read(ref open));
            }
        }

        private sealed class SlowGoodbyeConnection(TimeSpan goodbye, Action closed) : IRfcommConnection
        {
            private readonly MemoryStream stream = new();

            public string RemoteName => "somewhere, slowly";

            public bool IsClosed { get; private set; }

            public Stream Stream => stream;

            public void Close() => Dispose();

            public void Dispose()
            {
                Thread.Sleep(goodbye);
                IsClosed = true;
                closed();
            }
        }
    }

    private sealed class FakeConnector(Func<CancellationToken, Task<IRfcommConnection>> dial) : IRfcommConnector
    {
        public string? DialledAddress { get; private set; }

        public Guid DialledService { get; private set; }

        public Task<IRfcommConnection> ConnectAsync(string deviceAddress, Guid serviceId, CancellationToken cancel)
        {
            DialledAddress = deviceAddress;
            DialledService = serviceId;
            return dial(cancel);
        }
    }

    private sealed class FakeFinder(string? localAddress) : IDeviceFinder
    {
        public List<RfcommDevice> RememberedDevices { get; } = [];

        public List<RfcommDevice> NearbyDevices { get; } = [];

        public bool InquiryWasAsked { get; private set; }

        public bool StopAfterFirstNearby { get; set; }

        public Exception? InquiryThrows { get; set; }

        public Exception? RememberedThrows { get; set; }

        public IReadOnlyList<RfcommDevice> Remembered()
        {
            if (RememberedThrows is { } failure)
            {
                throw failure;
            }

            return RememberedDevices;
        }

        public async IAsyncEnumerable<RfcommDevice> NearbyAsync(
            TimeSpan timeout,
            [EnumeratorCancellation] CancellationToken cancel = default)
        {
            InquiryWasAsked = true;

            if (InquiryThrows is { } failure)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            foreach (var device in NearbyDevices)
            {
                cancel.ThrowIfCancellationRequested();
                yield return device;

                if (StopAfterFirstNearby)
                {
                    cancel.ThrowIfCancellationRequested();
                    throw new OperationCanceledException(cancel);
                }
            }

            await Task.CompletedTask;
        }

        public string? LocalAddress() => localAddress;
    }
}
