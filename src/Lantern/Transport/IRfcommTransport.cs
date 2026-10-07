namespace Lantern.Transport;

/// <summary>
/// One RFCOMM channel, accepted or connected, seen through the small seam the transport classes work
/// against.
/// </summary>
/// <remarks>
/// The seam exists because a test suite cannot pair laptops (T4.1), but the parts of this milestone
/// that can go wrong — the accept loop, dropping links when the host quits, who wins a race between a
/// timeout and a connect, which devices a scan is allowed to list — are all logic, and logic gets
/// tested. The real implementation is <see cref="ThreeFeetRfcommListener"/>,
/// <see cref="ThreeFeetRfcommConnector"/> and <see cref="ThreeFeetDeviceFinder"/>, and those are what
/// <c>tools/BtSpike</c> exercises by hand on hardware.
/// </remarks>
public interface IRfcommConnection : IDisposable
{
    /// <summary>The duplex RFCOMM stream. The frame codec is told nothing else about it.</summary>
    Stream Stream { get; }

    /// <summary>Who is on the other end, as the stack described it.</summary>
    string RemoteName { get; }

    /// <summary>Idempotent.</summary>
    void Close();
}

/// <summary>Publishes the Lantern service and waits for someone to connect to it.</summary>
public interface IRfcommListener : IDisposable
{
    /// <summary>True once <see cref="Start"/> has succeeded and <see cref="Stop"/> has not run.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Register the service with the local SDP database and start listening. Throws with the stack's
    /// own words when Windows refuses — which is the first thing the field test needs to see
    /// (docs/testing/bluetooth-pairing-how-to.md).
    /// </summary>
    void Start(Guid serviceId, string serviceName);

    /// <summary>Next inbound channel, or null once the listener is stopped.</summary>
    Task<IRfcommConnection?> AcceptAsync(CancellationToken cancel);

    /// <summary>Remove the service registration and unblock any waiting <see cref="AcceptAsync"/>.</summary>
    void Stop();
}

/// <summary>Opens a channel to a service on another machine.</summary>
public interface IRfcommConnector
{
    /// <summary>
    /// Dial <paramref name="serviceId"/> on <paramref name="deviceAddress"/>.
    /// </summary>
    /// <exception cref="System.IO.IOException">the machine is not there, or does not host the service.</exception>
    /// <exception cref="TimeoutException">it did not answer in time.</exception>
    Task<IRfcommConnection> ConnectAsync(string deviceAddress, Guid serviceId, CancellationToken cancel);
}

/// <summary>
/// A machine the radio can see. <see cref="Remembered"/> is the difference between "this laptop has
/// been set up with that one" and "something answered an inquiry": only remembered machines are
/// usually worth dialling (T4.1 D2).
/// </summary>
public sealed record class RfcommDevice(string Address, string Name, bool Remembered);

/// <summary>
/// Where candidate machines come from. There is no way to ask a Windows stack "who hosts the Lantern
/// service" (measured in T4.1), so discovery is paired devices plus an inquiry, and the join attempt is
/// what turns out to be the probe.
/// </summary>
public interface IDeviceFinder
{
    /// <summary>Machines this one is paired with. Answers immediately.</summary>
    IReadOnlyList<RfcommDevice> Remembered();

    /// <summary>Machines that answer an inquiry, as they arrive, until the timeout or the token ends it.</summary>
    IAsyncEnumerable<RfcommDevice> NearbyAsync(TimeSpan timeout, CancellationToken cancel);

    /// <summary>
    /// This machine's own address, so that a scan can leave itself out of its own results. Null when
    /// the stack will not say, which is a reason to be tolerant rather than a crash.
    /// </summary>
    string? LocalAddress();
}
