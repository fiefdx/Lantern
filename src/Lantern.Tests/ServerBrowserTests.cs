using System.Diagnostics;
using Lantern.Core;
using Lantern.Storage;
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
        var found = await ServerBrowser.DiscoverAsync(
            TimeSpan.FromSeconds(30),
            new StubClient([]),
            cancel: cancel.Token);

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

    // ------------------------------------------------------------------ T4.3

    [Theory]
    [InlineData("F8E4E3F9603B")]
    [InlineData("f8-e4-e3-f9-60-3b")]
    [InlineData("f8:e4:e3:f9:60:3b")]      // some Windows builds write an address with colons
    [InlineData("F8 E4 E3 F9 60 3B")]
    public void ATypedBluetoothAddressIsAcceptedInEveryShapeItIsWrittenIn(string text)
    {
        Assert.True(ServerBrowser.TryParseAddress(text, out var endpoint, out _));

        // Normalised to twelve upper-case digits, because that is what the dialler needs and what the
        // known-hosts file ends up holding.
        Assert.Equal("F8E4E3F9603B", endpoint!.DeviceAddress);
        Assert.Equal("F8E4E3F9603B", endpoint.ServerName);   // an address is the only name typing gives
        Assert.Equal(LanternProtocol.ServiceId, endpoint.ServiceId);
    }

    [Fact]
    public void AHostNamedExactlyLikeAnAddressIsReadAsAnAddress()
    {
        // Recorded because it is a real trade: "F8E4E3F9603B" has no colon, so the alternative reading is
        // a host name on the default port. A machine named like twelve hex digits loses to a paste from
        // the Settings app, which is what a person is actually holding.
        Assert.True(ServerBrowser.TryParseAddress("ABCDEF012345", out var endpoint, out _));
        Assert.Equal("ABCDEF012345", endpoint!.DeviceAddress);
        Assert.DoesNotContain(":", endpoint.DeviceAddress, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddressThatIsNeitherShapeIsRefusedWithBothExamples()
    {
        Assert.False(ServerBrowser.TryParseAddress("   ", out _, out var blank));
        Assert.Contains("F8E4E3F9603B", blank, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:43797", blank, StringComparison.Ordinal);

        // A bare word is not refused: over loopback it means "that host, on the default port", which is
        // how the two-process development mode is driven every day (T2.6). It becomes a Bluetooth address
        // in the join box and the dialler says so, which is a worse waste of an afternoon than a name.
        Assert.True(ServerBrowser.TryParseAddress("laptop-two", out var bare, out _));
        Assert.Equal($"laptop-two:{LoopbackPorts.BasePort}", bare!.DeviceAddress);

        // Somebody who typed "laptop:" has not typed an empty port; they have not finished typing, and
        // telling them `"" is not a port number` is no help at all.
        Assert.False(ServerBrowser.TryParseAddress("laptop:", out _, out var noPort));
        Assert.Contains("laptop:", noPort, StringComparison.Ordinal);
        Assert.Contains("port number", noPort, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhatWasRememberedIsOfferedUnderneathWhatAnswered()
    {
        var answered = new ServerEndpoint("Lantern on port 43797", "127.0.0.1:43797", LanternProtocol.ServiceId);
        var answeredToo = new ServerEndpoint("Keyboard K480", "34885DBDCC01", LanternProtocol.ServiceId);
        var remembered = new ServerEndpoint("haitao-iPhone", "AC86A3294B28", LanternProtocol.ServiceId);
        var rememberedButAnswering = remembered with { DeviceAddress = "34885DBDCC01", ServerName = "called something else last term" };

        var found = await ServerBrowser.DiscoverAsync(
            TimeSpan.FromMilliseconds(200),
            new StubClient([answered, answeredToo]),
            new StubKnownHosts([rememberedButAnswering, remembered]));

        // Discovery first, because it is the fresher evidence that a machine is in the room; a remembered
        // host that also answered is listed once, under the name the radio reported today.
        Assert.Equal(
            new[] { "Lantern on port 43797", "Keyboard K480", "haitao-iPhone" },
            found.Select(e => e.ServerName));
        Assert.Equal(
            new[] { "127.0.0.1:43797", "34885DBDCC01", "AC86A3294B28" },
            found.Select(e => e.DeviceAddress));
    }

    [Fact]
    public async Task AStoppedScanStillShowsTheMachinesKnownFromBefore()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var found = await ServerBrowser.DiscoverAsync(
            TimeSpan.FromSeconds(30),
            new StubClient([]),
            new StubKnownHosts([new ServerEndpoint("haitao-iPhone", "AC86A3294B28", LanternProtocol.ServiceId)]),
            cancel.Token);

        // The scan was stopped, not the question. On Windows the machine a person wants is usually in this
        // list rather than in the radio's answer (T4.1), so throwing the list away with the scan would
        // leave an empty window and no way to join at all.
        Assert.Single(found);
    }

    [Fact]
    public async Task AHistoryThatWillNotOpenIsNoHistoryRatherThanNoScan()
    {
        var found = await ServerBrowser.DiscoverAsync(
            TimeSpan.FromMilliseconds(200),
            new StubClient([new ServerEndpoint("Lantern on port 43797", "127.0.0.1:43797", LanternProtocol.ServiceId)]),
            new StubKnownHosts(null));

        Assert.Single(found);
    }

    /// <summary>An <see cref="IKnownHosts"/> that holds a list, or refuses to talk about one.</summary>
    private sealed class StubKnownHosts(IReadOnlyList<ServerEndpoint>? recent) : IKnownHosts
    {
        public event EventHandler<string>? SaveFailed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<ServerEndpoint> Recent()
            => recent ?? throw new IOException("the history file is on a disk that is not there");

        public void Remember(ServerEndpoint endpoint) => throw new NotSupportedException();

        public Task SavesCompletedAsync() => Task.CompletedTask;
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
