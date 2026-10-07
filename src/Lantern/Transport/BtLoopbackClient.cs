using System.Net;
using System.Net.Sockets;
using Lantern.Core;

namespace Lantern.Transport;

/// <summary>
/// The client side over localhost TCP (T2.3). Dialling an endpoint is a plain TCP connect;
/// "discovery" walks the small loopback port range and reports whichever ports answer, which is the
/// loopback stand-in for the Bluetooth scan that arrives in M4 as
/// <see cref="IBtClient.DiscoverAsync"/> on <c>BtRfcommClient</c>.
/// </summary>
public sealed class BtLoopbackClient : IBtClient
{
    /// <summary>How long a single port is given to answer during a scan.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(250);

    /// <inheritdoc />
    public async Task<IClientLink> ConnectAsync(ServerEndpoint endpoint, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!LoopbackPorts.TryParseAddress(endpoint.DeviceAddress, out var host, out var port))
        {
            throw new FormatException($"\"{endpoint.DeviceAddress}\" is not a host:port address.");
        }

        var client = new TcpClient { NoDelay = true };

        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (SocketException ex)
        {
            client.Dispose();
            throw new IOException($"Could not reach {endpoint.DisplayName}.", ex);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"Timed out reaching {endpoint.DisplayName}.");
        }

        return new LoopbackClientLink(client);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerEndpoint>> DiscoverAsync(TimeSpan timeout, CancellationToken cancel = default)
    {
        var budget = timeout <= TimeSpan.Zero ? ProbeTimeout : timeout;
        var found = new List<ServerEndpoint>();
        var tasks = LoopbackPorts.All()
            .Select(port => ProbeAsync(port, budget, cancel))
            .ToArray();

        foreach (var task in tasks)
        {
            // Every port is probed at once, so the whole scan costs about one timeout, not eight.
            var endpoint = await task.ConfigureAwait(false);
            if (endpoint is not null)
            {
                found.Add(endpoint);
            }
        }

        return found;
    }

    /// <summary>
    /// Opens a socket, then closes it again without saying anything. A host that is up sees a
    /// connection that dies before HELLO and drops it silently — see <c>ChatServer</c>.
    /// </summary>
    private static async Task<ServerEndpoint?> ProbeAsync(int port, TimeSpan budget, CancellationToken cancel)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(budget);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);

            await client.ConnectAsync(IPAddress.Loopback, port, linked.Token).ConfigureAwait(false);

            // A port that answers is a Lantern host as far as loopback can tell; the name is filled
            // in from the port so the list still reads meaningfully.
            return new ServerEndpoint($"Lantern on port {port}", LoopbackPorts.Address(port), Guid.Empty);
        }
        catch (SocketException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
