using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Lantern.Core;

namespace Lantern.Transport;

/// <summary>
/// How a Lantern host is reached over the loopback transport.
/// <para>
/// A real Bluetooth host is found by scanning (M4). On one machine there is nothing to scan, so
/// loopback hosts sit on a small fixed port range and <see cref="BtLoopbackClient"/> walks it —
/// "a TCP probe", which is what T2.6 allows for the loopback version of discovery. The port range
/// is deliberately tiny so scanning stays a fraction of a second.
/// </para>
/// </summary>
public static class LoopbackPorts
{
    /// <summary>First port a loopback host may take.</summary>
    public const int BasePort = 43797;

    /// <summary>How many consecutive ports a host may use and a scan will probe.</summary>
    public const int Range = 8;

    /// <summary>The address half of a <see cref="ServerEndpoint"/> for a loopback host.</summary>
    public static string Address(int port) => $"127.0.0.1:{port}";

    /// <summary>Splits the address half of a <see cref="ServerEndpoint"/> back into host and port.</summary>
    public static bool TryParseAddress(string? address, out string host, out int port)
    {
        host = "127.0.0.1";
        port = 0;

        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var separator = address.LastIndexOf(':');
        if (separator <= 0)
        {
            // No port given: treat the whole thing as a host on the base port.
            host = address;
            port = BasePort;
            return true;
        }

        host = address[..separator];
        return int.TryParse(address[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
               && port is (> 0 and <= 65535);
    }

    /// <summary>Every port a scan probes.</summary>
    public static IEnumerable<int> All()
    {
        for (var port = BasePort; port < BasePort + Range; port++)
        {
            yield return port;
        }
    }
}

/// <summary>
/// The host side over localhost TCP (T2.3): bind a port, accept connections, hand each one up as an
/// <see cref="IClientLink"/>. <c>ChatServer</c> drives this in a loop and never learns it is TCP.
/// </summary>
public sealed class BtLoopbackServer : IBtServer
{
    private readonly List<LoopbackClientLink> links = [];
    private readonly object gate = new();
    private CancellationTokenSource? acceptCancellation;
    private TcpListener? listener;

    public BtLoopbackServer(string serverName, int? port = null)
    {
        ServerName = string.IsNullOrWhiteSpace(serverName) ? Environment.MachineName : serverName.Trim();
        RequestedPort = port;
    }

    /// <summary>Name announced to clients (what their server list shows).</summary>
    public string ServerName { get; }

    /// <summary>Port asked for in the constructor; null means "the first free one in the range".</summary>
    public int? RequestedPort { get; }

    /// <summary>Port actually bound, valid once <see cref="StartAsync"/> has completed.</summary>
    public int Port { get; private set; }

    /// <summary>Service id handed to <see cref="StartAsync"/>; unused by TCP, kept for parity with RFCOMM.</summary>
    public Guid ServiceId { get; private set; }

    /// <inheritdoc />
    public bool IsListening { get; private set; }

    /// <summary>What a client needs to dial this host.</summary>
    public ServerEndpoint Endpoint => new(ServerName, LoopbackPorts.Address(Port), ServiceId);

    /// <summary>Connections accepted so far and still open.</summary>
    public IReadOnlyList<IClientLink> Links
    {
        get
        {
            lock (gate)
            {
                return links.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(Guid serviceId)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(BtLoopbackServer));
        }

        lock (gate)
        {
            if (IsListening)
            {
                return Task.CompletedTask;
            }

            ServiceId = serviceId;
            acceptCancellation = new CancellationTokenSource();

            // The range exists because a second instance on the same machine must still be able to
            // host: the first free port wins instead of the second instance failing to start.
            var candidates = RequestedPort is { } asked
                ? new[] { asked }
                : LoopbackPorts.All();

            TcpListener? bound = null;
            Exception? last = null;

            foreach (var port in candidates)
            {
                try
                {
                    var candidate = new TcpListener(IPAddress.Loopback, port);
                    candidate.Start();
                    bound = candidate;
                    Port = port;
                    break;
                }
                catch (SocketException ex)
                {
                    last = ex;
                }
            }

            if (bound is null)
            {
                throw new IOException(
                    $"Could not listen on ports {LoopbackPorts.BasePort}-{LoopbackPorts.BasePort + LoopbackPorts.Range - 1}.",
                    last);
            }

            listener = bound;
            IsListening = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IClientLink?> AcceptAsync(CancellationToken cancel = default)
    {
        var current = listener;
        var source = acceptCancellation;

        if (current is null || source is null)
        {
            return null;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, source.Token);

        TcpClient client;
        try
        {
            client = await current.AcceptTcpClientAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped, or the caller is going away: either way there is no link to report.
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        var link = new LoopbackClientLink(client);

        lock (gate)
        {
            if (disposed)
            {
                link.Close();
                return null;
            }

            links.Add(link);
        }

        link.Closed += (_, _) =>
        {
            lock (gate)
            {
                links.Remove(link);
            }
        };

        return link;
    }

    /// <inheritdoc />
    public void Stop()
    {
        CancellationTokenSource? source;
        TcpListener? current;
        LoopbackClientLink[] open;

        lock (gate)
        {
            if (!IsListening && listener is null)
            {
                return;
            }

            IsListening = false;
            source = acceptCancellation;
            current = listener;
            open = links.ToArray();
            links.Clear();
            acceptCancellation = null;
            listener = null;
        }

        source?.Cancel();

        try
        {
            current?.Stop();
        }
        catch (SocketException)
        {
            // Already gone; nothing to release.
        }

        // Scenario 6: quitting the server drops every client, which is what makes their
        // "connection lost" path run.
        foreach (var link in open)
        {
            link.Close();
        }

        source?.Dispose();
    }

    private bool disposed;

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Stop();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
