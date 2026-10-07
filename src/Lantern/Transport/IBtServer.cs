namespace Lantern.Transport;

/// <summary>
/// Server half of a transport. Implemented by <c>BtLoopbackServer</c> (M2, localhost TCP) and
/// <c>BtRfcommServer</c> (M4, real RFCOMM); the UI and <c>ChatServer</c> never see the difference.
/// </summary>
public interface IBtServer : IAsyncDisposable
{
    /// <summary>Start listening. <paramref name="serviceId"/> is the advertised Lantern service GUID.</summary>
    Task StartAsync(Guid serviceId);

    /// <summary>
    /// Wait for the next inbound connection. Returns null when the server has been stopped.
    /// Callers loop over this (see <c>ChatServer.AcceptLoop</c>).
    /// </summary>
    Task<IClientLink?> AcceptAsync(CancellationToken cancel = default);

    /// <summary>Stop listening and drop accepted links.</summary>
    void Stop();

    /// <summary>True while <see cref="StartAsync"/> has succeeded and Stop has not been called.</summary>
    bool IsListening { get; }
}
