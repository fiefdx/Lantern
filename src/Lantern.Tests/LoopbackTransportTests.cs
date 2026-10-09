using Lantern.Core;
using Lantern.Domain;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T2.3: the localhost-TCP stand-in for the Bluetooth link. These tests only care that the
/// <see cref="IBtServer"/> / <see cref="IBtClient"/> / <see cref="IClientLink"/> contracts are
/// honoured — the whole point is that <c>ChatServer</c> and <c>ChatClient</c> will not be able to
/// tell this transport from RFCOMM in M4.
/// </summary>
public class LoopbackTransportTests
{
    [Fact]
    public async Task AServerAcceptsWhatAClientDials()
    {
        await using var server = new BtLoopbackServer("Host A");
        await server.StartAsync(Guid.NewGuid());

        Assert.True(server.IsListening);
        Assert.InRange(server.Port, LoopbackPorts.BasePort, LoopbackPorts.BasePort + LoopbackPorts.Range - 1);

        var accept = server.AcceptAsync();
        using var link = await new BtLoopbackClient().ConnectAsync(server.Endpoint);

        var accepted = await accept;

        Assert.NotNull(accepted);
        Assert.NotEmpty(accepted!.ConnectionId);
        Assert.Single(server.Links);

        accepted.Close();
        Assert.Empty(server.Links);
    }

    [Fact]
    public async Task FramesCrossTheLinkInBothDirections()
    {
        await using var server = new BtLoopbackServer("Host A");
        await server.StartAsync(Guid.NewGuid());

        var accept = server.AcceptAsync();
        using var clientLink = (LoopbackClientLink?)await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        var serverLink = await accept;

        await FrameCodec.WriteAsync(clientLink!.Output, Frame.Hello("Haitao"));
        var hello = await FrameCodec.ReadAsync(serverLink!.Input);

        Assert.Equal(FrameType.HELLO, hello!.Type);
        Assert.Equal("Haitao", hello.Header.Nickname);

        await FrameCodec.WriteAsync(serverLink.Output, Frame.HelloAck("Haitao"));
        var ack = await FrameCodec.ReadAsync(clientLink.Input);

        Assert.Equal(FrameType.HELLO_ACK, ack!.Type);

        // And a frame with a binary body, since that is what stresses a real stream.
        var body = new byte[40_000];
        new Random(5).NextBytes(body);
        await FrameCodec.WriteAsync(clientLink.Output, Frame.Picture(Guid.NewGuid(), new Room("Room 1"), "Haitao", "a.png", body, DateTime.Now));

        var picture = await FrameCodec.ReadAsync(serverLink.Input);

        Assert.Equal(body, picture!.Body);
    }

    [Fact]
    public async Task ManySmallFramesArriveInOrderOnARealSocket()
    {
        // The codec tests cover the split cases against a MemoryStream; this proves the same thing
        // over a socket, where the OS decides how much of a write arrives in one read.
        await using var server = new BtLoopbackServer("Host A");
        await server.StartAsync(Guid.NewGuid());

        var accept = server.AcceptAsync();
        using var clientLink = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        var serverLink = await accept;

        // Written one after another on purpose: concurrent writes to one stream would be free to
        // interleave their bytes, which is what a session's write queue is for (T2.4).
        for (var i = 0; i < 50; i++)
        {
            await FrameCodec.WriteAsync(clientLink!.Output, Frame.Text(Guid.NewGuid(), new Room("Room 1"), "Haitao", $"m{i}", DateTime.Now));
        }

        var read = new List<string?>();
        for (var i = 0; i < 50; i++)
        {
            var frame = await FrameCodec.ReadAsync(serverLink!.Input);
            read.Add(frame?.Header.Text);
        }

        Assert.Equal(Enumerable.Range(0, 50).Select(i => $"m{i}"), read);
    }

    [Fact]
    public async Task ClosingTheLinkTellsBothEnds()
    {
        await using var server = new BtLoopbackServer("Host A");
        await server.StartAsync(Guid.NewGuid());

        var accept = server.AcceptAsync();
        var clientLink = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        var serverLink = await accept;

        var closes = 0;
        clientLink.Closed += (_, _) => closes++;

        clientLink.Close();
        clientLink.Close();

        Assert.Equal(1, closes);

        // The other end sees a clean end of stream, which is how a read loop learns the peer hung up.
        Assert.Null(await FrameCodec.ReadAsync(serverLink!.Input));
    }

    [Fact]
    public async Task StoppingTheServerDropsEveryOpenConnection()
    {
        await using var server = new BtLoopbackServer("Host A");
        await server.StartAsync(Guid.NewGuid());

        var accept = server.AcceptAsync();
        using var clientLink = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        var serverLink = await accept;

        var serverLinkClosed = false;
        serverLink!.Closed += (_, _) => serverLinkClosed = true;

        server.Stop();

        Assert.False(server.IsListening);
        Assert.Empty(server.Links);
        Assert.True(serverLinkClosed);

        // The client's read loop must see the server go away rather than wait forever (scenario 6).
        Assert.Null(await FrameCodec.ReadAsync(clientLink.Input));

        // Reading a link this process closed itself is not "end of stream" but "object disposed":
        // a read loop has to treat both as the connection ending, which is what ChatServer's and
        // ChatClient's loops do.
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => FrameCodec.ReadAsync(serverLink.Input)!);
    }

    [Fact]
    public async Task AcceptReturnsNullOnceTheServerIsStopped()
    {
        await using var server = new BtLoopbackServer("Host A");
        await server.StartAsync(Guid.NewGuid());

        var accept = server.AcceptAsync();
        server.Stop();

        Assert.Null(await accept);
    }

    [Fact]
    public async Task TwoHostsOnOneMachineGetDifferentPorts()
    {
        // The demo needs a host and a client in one process; the second listener must not fail.
        await using var first = new BtLoopbackServer("Host A");
        await using var second = new BtLoopbackServer("Host B");

        await first.StartAsync(Guid.NewGuid());
        await second.StartAsync(Guid.NewGuid());

        Assert.NotEqual(first.Port, second.Port);
        Assert.Equal($"127.0.0.1:{second.Port}", second.Endpoint.DeviceAddress);
        Assert.Contains("127.0.0.1", second.Endpoint.DisplayName);
    }

    [Fact]
    public async Task AScanFindsAHostThatIsUpAndIgnoresPortsThatAreNot()
    {
        await using var server = new BtLoopbackServer("Host A");
        await server.StartAsync(Guid.NewGuid());

        var found = await new BtLoopbackClient().DiscoverAsync(TimeSpan.FromSeconds(2));

        Assert.Contains(found, e => e.DeviceAddress == server.Endpoint.DeviceAddress);
        Assert.All(found, e => Assert.StartsWith("127.0.0.1:", e.DeviceAddress));

        // A scan must not leave the host thinking someone joined.
        var accepted = await server.AcceptAsync();
        accepted?.Close();
    }

    [Fact]
    public async Task DiallingAPortNobodyIsOnFailsAsAnIOError()
    {
        var endpoint = new ServerEndpoint("nowhere", LoopbackPorts.Address(LoopbackPorts.BasePort + LoopbackPorts.Range - 1), Guid.Empty);

        await Assert.ThrowsAnyAsync<IOException>(() => new BtLoopbackClient().ConnectAsync(endpoint));
    }

    [Fact]
    public async Task RestartingAHostOnTheSamePortWorks()
    {
        var server = new BtLoopbackServer("Host A");
        await server.StartAsync(Guid.NewGuid());
        var port = server.Port;

        await server.DisposeAsync();

        await using var again = new BtLoopbackServer("Host A", port);
        await again.StartAsync(Guid.NewGuid());

        Assert.Equal(port, again.Port);

        var accept = again.AcceptAsync();
        using var link = await new BtLoopbackClient().ConnectAsync(again.Endpoint);
        Assert.NotNull(await accept);
    }

    [Theory]
    [InlineData("127.0.0.1:43797", "127.0.0.1", 43797)]
    [InlineData("host-a", "host-a", 43797)]
    [InlineData("", null, 0)]
    [InlineData("127.0.0.1:notaport", null, 0)]
    public void AddressesRoundTripThroughTheEndpointString(string address, string? host, int port)
    {
        var ok = LoopbackPorts.TryParseAddress(address, out var parsedHost, out var parsedPort);

        Assert.Equal(host is not null, ok);

        if (ok)
        {
            Assert.Equal(host, parsedHost);
            Assert.Equal(port, parsedPort);
        }
        else
        {
            Assert.Equal(0, parsedPort);
        }
    }
}
