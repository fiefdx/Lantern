using Lantern.Domain;
using Lantern.Storage;
using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// Answers “which Lantern chats can I join?” (F2). Over a radio that is a paired list plus an inquiry
/// (<see cref="BtRfcommClient"/>); over loopback it is a probe of a small port range, which is what lets
/// one laptop and the whole test suite behave as though there were a roomful of machines. Either way the
/// caller gets <see cref="ServerEndpoint"/>s and never sees a socket.
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
    /// <param name="client">The transport to scan with; the startup window passes the Bluetooth one.</param>
    /// <param name="knownHosts">
    /// Machines joined before, offered underneath whatever the scan found. Passing this is what makes the
    /// list useful on Windows, where the machine you want is paired but not discoverable (T4.1).
    /// </param>
    /// <param name="cancel">Stops the scan and returns what has been found so far.</param>
    public static async Task<IReadOnlyList<ServerEndpoint>> DiscoverAsync(
        TimeSpan? timeout = null,
        IBtClient? client = null,
        IKnownHosts? knownHosts = null,
        CancellationToken cancel = default)
    {
        var ownsClient = client is null;
        var transport = client ?? new BtLoopbackClient();

        IReadOnlyList<ServerEndpoint> discovered;

        try
        {
            discovered = await transport.DiscoverAsync(timeout ?? DefaultTimeout, cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            discovered = [];
        }
        finally
        {
            if (ownsClient && transport is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        return Merge(discovered, knownHosts);
    }

    /// <summary>
    /// What was found, then what is remembered, each machine once.
    /// </summary>
    /// <remarks>
    /// Discovery order wins because it is the freshest evidence a machine is actually there. A remembered
    /// host that also answered keeps the name the scan gave it, since that is the name its own radio is
    /// reporting today. Nothing is filtered out for being a loopback address or a keyboard: the list shows
    /// an address on every line, and a person decides which line is the friend.
    /// </remarks>
    private static IReadOnlyList<ServerEndpoint> Merge(IReadOnlyList<ServerEndpoint> discovered, IKnownHosts? knownHosts)
    {
        // Two Lanterns could advertise the same address; the list must not offer the same host twice.
        var merged = new List<ServerEndpoint>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in discovered)
        {
            if (seen.Add(endpoint.DeviceAddress))
            {
                merged.Add(endpoint);
            }
        }

        if (knownHosts is null)
        {
            return merged;
        }

        try
        {
            foreach (var endpoint in knownHosts.Recent())
            {
                if (seen.Add(endpoint.DeviceAddress))
                {
                    merged.Add(endpoint);
                }
            }
        }
        catch (Exception)
        {
            // A history file that will not open is no history. The scan still stands on its own.
        }

        return merged;
    }

    /// <summary>
    /// Types in an address by hand (F2's fallback when the scan finds nothing, and over a radio often the
    /// only way - T4.1 D2). Accepts a Bluetooth address in any of the shapes Windows and the Settings app
    /// write it in, and for loopback <c>host</c>, <c>host:port</c> and <c>127.0.0.1:43797</c>; a bare host
    /// takes the first port of the range.
    /// </summary>
    public static bool TryParseAddress(string? typed, out ServerEndpoint? endpoint, out string? error)
    {
        endpoint = null;
        error = null;

        var text = ValidationRules.Normalize(typed);
        if (text.Length == 0)
        {
            error = "Type the host's address, for example F8E4E3F9603B or 127.0.0.1:43797.";
            return false;
        }

        // Checked first, and before the colon is treated as a port: "F8:E4:E3:F9:60:3B" is an address some
        // Windows builds write with colons, and reading "3B" as a port number would be a confident waste
        // of somebody's afternoon. The cost is that a machine named like twelve hex digits is read as an
        // address, which is the rarer machine by a long way.
        if (RfcommAddress.TryParse(text, out var address))
        {
            var bluetooth = RfcommAddress.Format(address);
            endpoint = new ServerEndpoint(bluetooth, bluetooth, LanternProtocol.ServiceId);
            return true;
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
                // Someone who typed "laptop:" has not typed an empty port; they have not finished typing.
                error = portText.Length == 0
                    ? $"\"{text}\" needs a port number after the colon."
                    : $"\"{portText}\" is not a port number.";
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
