using Lantern.Domain;
using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// Answers “which Lantern chats can I join?” (F2). Over Bluetooth in M4 this becomes an RFCOMM
/// service scan; for now it probes the loopback port range, which is the stand-in the task list
/// allows for M2. Either way the caller gets <see cref="ServerEndpoint"/>s and never sees a socket.
/// </summary>
public static class ServerBrowser
{
    /// <summary>
    /// How long a single scan waits. Short because the user is standing at the startup window
    /// pressing “Scan”, and on loopback every port is probed at once.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// One scan. An empty list is a normal answer, not an error: nothing is hosting, or the host is
    /// on another machine and loopback cannot see it.
    /// </summary>
    /// <param name="timeout">How long to wait for each probe.</param>
    /// <param name="client">The transport to scan with; M4 swaps this for the Bluetooth one.</param>
    /// <param name="cancel">Stops the scan and returns what has been found so far.</param>
    public static async Task<IReadOnlyList<ServerEndpoint>> DiscoverAsync(
        TimeSpan? timeout = null,
        IBtClient? client = null,
        CancellationToken cancel = default)
    {
        var ownsClient = client is null;
        var transport = client ?? new BtLoopbackClient();

        try
        {
            var found = await transport.DiscoverAsync(timeout ?? DefaultTimeout, cancel).ConfigureAwait(false);

            // Two Lanterns could advertise the same address; the list must not offer the same host twice.
            return found.Distinct().ToArray();
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        finally
        {
            if (ownsClient && transport is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// Types in an address by hand (F2's fallback when the scan finds nothing, and the only way to
    /// reach a host on another machine). Accepts <c>host</c>, <c>host:port</c> and
    /// <c>127.0.0.1:43797</c>; a bare host takes the first port of the range.
    /// </summary>
    public static bool TryParseAddress(string? typed, out ServerEndpoint? endpoint, out string? error)
    {
        endpoint = null;
        error = null;

        var text = ValidationRules.Normalize(typed);
        if (text.Length == 0)
        {
            error = "Type the host's address, for example 127.0.0.1:43797.";
            return false;
        }

        // IPv6 literals are not handled: an address typed as a host name is far more likely here,
        // and a colon already means "host:port".
        var separator = text.LastIndexOf(':');

        if (separator >= 0)
        {
            var host = text[..separator].Trim();
            var portText = text[(separator + 1)..].Trim();

            if (host.Length == 0)
            {
                error = "The address needs a host before the colon.";
                return false;
            }

            if (!int.TryParse(portText, out var port))
            {
                error = $"\"{portText}\" is not a port number.";
                return false;
            }

            if (port is < 1 or > 65535)
            {
                error = "Port must be between 1 and 65535.";
                return false;
            }

            endpoint = new ServerEndpoint(host, $"{host}:{port}", LanternProtocol.ServiceId);
            return true;
        }

        // A bare host name: the first port of the range, which is what the only host uses.
        endpoint = new ServerEndpoint(text, $"{text}:{LoopbackPorts.BasePort}", LanternProtocol.ServiceId);
        return true;
    }
}
