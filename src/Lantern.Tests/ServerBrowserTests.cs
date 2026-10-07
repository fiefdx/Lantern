using System.Diagnostics;
using Lantern.Core;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T2.6: the list on the startup window. On loopback “discovery” is a TCP probe of the port range,
/// so these tests are about what the scan promises rather than about radio hardware.
/// </summary>
public sealed class ServerBrowserTests
{
    [Fact]
    public async Task AScanFindsTheHostThatIsListening()
    {
        var transport = new BtLoopbackServer("Kitchen Table");
        await transport.StartAsync(LanternProtocol.ServiceId);

        try
        {
            var found = await ServerBrowser.DiscoverAsync();

            var host = Assert.Single(found, e => e.DeviceAddress == LoopbackPorts.Address(transport.Port));
            // A probe only learns that something answers, so the name in the list is a placeholder and
            // the host's real name arrives in HELLO_ACK (see TheHostTellsTheClientWhatToCallIt). Over
            // Bluetooth in M4 the scan itself carries the device name.
            Assert.Equal($"Lantern on port {transport.Port}", host.ServerName);
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task AScanWithNothingHostingIsAnEmptyListRatherThanAnError()
    {
        var found = await ServerBrowser.DiscoverAsync(TimeSpan.FromMilliseconds(200));

        Assert.Empty(found);
    }

    [Fact]
    public async Task TheSameHostIsNeverOfferedTwice()
    {
        var twice = new ServerEndpoint("Dup", "127.0.0.1:1", Guid.Empty);
        var browser = new StubClient([twice, twice]);

        var found = await ServerBrowser.DiscoverAsync(TimeSpan.FromMilliseconds(200), browser);

        Assert.Single(found);
    }

    [Fact]
    public async Task ACancelledScanComesBackRatherThanHanging()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        // Pressing "Scan" and then closing the window must not leave everything waiting for a scan
        // that was asked to take thirty seconds.
        var started = Stopwatch.StartNew();
        var found = await ServerBrowser.DiscoverAsync(TimeSpan.FromSeconds(30), new StubClient([]), cancel.Token);

        Assert.Empty(found);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2), $"the scan took {started.Elapsed} to give up");
    }

    [Fact]
    public void ATypedAddressIsAcceptedInEveryShapeTheManualSuggests()
    {
        Assert.True(ServerBrowser.TryParseAddress("127.0.0.1:43799", out var typed, out _));
        Assert.Equal("127.0.0.1:43799", typed!.DeviceAddress);
        Assert.Equal("127.0.0.1", typed.ServerName);

        Assert.True(ServerBrowser.TryParseAddress("  laptop-two:43800  ", out var named, out _));
        Assert.Equal("laptop-two:43800", named!.DeviceAddress);

        // A host with no port gets the first port of the range, which is what a lone host binds.
        Assert.True(ServerBrowser.TryParseAddress("laptop-two", out var bare, out _));
        Assert.Equal($"laptop-two:{LoopbackPorts.BasePort}", bare!.DeviceAddress);
    }

    [Fact]
    public void ANonsenseAddressIsRefusedWithSomethingToRead()
    {
        Assert.False(ServerBrowser.TryParseAddress(null, out _, out var nothing));
        Assert.NotNull(nothing);

        Assert.False(ServerBrowser.TryParseAddress("   ", out _, out var blank));
        Assert.NotNull(blank);

        Assert.False(ServerBrowser.TryParseAddress(":43797", out _, out var noHost));
        Assert.Contains("host", noHost);

        Assert.False(ServerBrowser.TryParseAddress("laptop:22a", out _, out var notAPort));
        Assert.Contains("22a", notAPort);

        Assert.False(ServerBrowser.TryParseAddress("laptop:0", out _, out var zero));
        Assert.NotNull(zero);

        Assert.False(ServerBrowser.TryParseAddress("laptop:70000", out _, out var tooBig));
        Assert.Contains("65535", tooBig);
    }

    [Fact]
    public async Task TheHostTellsTheClientWhatToCallIt()
    {
        var server = new ChatServer(new BtLoopbackServer("Kitchen Table"), "Kitchen Table");
        await server.StartAsync();

        await using (server)
        {
            // Discovery can only report an address, so the client starts out calling the host by its
            // port. HELLO_ACK is where the host says its name.
            var dialed = server.Endpoint with { ServerName = "unknown" };

            await using var client = new ChatClient("Cara");
            await client.ConnectAsync(dialed);

            Assert.Equal("Kitchen Table", client.Endpoint!.ServerName);
        }
    }

    /// <summary>An <see cref="IBtClient"/> that reports a fixed list instead of touching the network.</summary>
    private sealed class StubClient(IReadOnlyList<ServerEndpoint> found) : IBtClient
    {
        public Task<IClientLink> ConnectAsync(ServerEndpoint endpoint, CancellationToken cancel = default)
            => throw new NotSupportedException("This test never connects.");

        // Waits like a real scan, so a cancelled token is honoured mid-scan rather than only before it.
        public async Task<IReadOnlyList<ServerEndpoint>> DiscoverAsync(TimeSpan timeout, CancellationToken cancel = default)
        {
            await Task.Delay(timeout, cancel).ConfigureAwait(false);
            return found;
        }
    }
}
