using Lantern.Core;

namespace Lantern.Transport;

/// <summary>
/// Client half of a transport, plus discovery. Implemented by <c>BtLoopbackClient</c> (M2) and
/// <c>BtRfcommClient</c> (M4).
/// </summary>
public interface IBtClient
{
    /// <summary>Open a connection to <paramref name="endpoint"/>.</summary>
    Task<IClientLink> ConnectAsync(ServerEndpoint endpoint, CancellationToken cancel = default);

    /// <summary>Look for Lantern hosts for up to <paramref name="timeout"/>.</summary>
    Task<IReadOnlyList<ServerEndpoint>> DiscoverAsync(TimeSpan timeout, CancellationToken cancel = default);
}
