using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Lantern.Core;

namespace Lantern.Transport;

/// <summary>
/// The joining side over real Bluetooth (T4.2): dial a machine, and list the machines worth dialling.
/// </summary>
/// <remarks>
/// Discovery is weaker than anybody would like, and the reason is a fact about this Windows stack rather
/// than a shortcut here: there is no way to ask which machines host a given service (measured in T4.1,
/// <c>BluetoothServiceCatalog: absent</c>). So a scan is "everything this machine is paired with, plus
/// whatever answers an inquiry", and the join attempt is what actually discovers a Lantern host — which
/// is why <see cref="ConnectAsync"/> has to fail with a sentence rather than a socket code
/// (<see cref="RfcommErrors"/>).
/// </remarks>
public sealed class BtRfcommClient : IBtClient
{
    /// <summary>
    /// How long a join attempt is given before the window is told it failed. The stack gives up on an
    /// unreachable machine at about five seconds (measured in T4.1), so this is above that rather than
    /// racing it: a scan that the user cancelled is a different message from a machine that did not
    /// answer.
    /// </summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(12);

    /// <summary>How long a scan inquires for when the caller did not say.</summary>
    public static readonly TimeSpan DefaultScanTimeout = TimeSpan.FromSeconds(12);

    /// <summary>
    /// A room full of laptops is not a list anybody can read, and an inquiry keeps answering for as long
    /// as it is allowed to.
    /// </summary>
    public const int MaxResults = 64;

    private readonly IRfcommConnector connector;
    private readonly IDeviceFinder finder;

    public BtRfcommClient(IRfcommConnector? connector = null, IDeviceFinder? finder = null, TimeSpan? connectTimeout = null)
    {
        this.connector = connector ?? new ThreeFeetRfcommConnector();
        this.finder = finder ?? new ThreeFeetDeviceFinder();
        ConnectTimeout = connectTimeout ?? DefaultConnectTimeout;
    }

    /// <summary>How long one join attempt may take.</summary>
    public TimeSpan ConnectTimeout { get; }

    /// <inheritdoc />
    public async Task<IClientLink> ConnectAsync(ServerEndpoint endpoint, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var address = RfcommAddress.Parse(endpoint.DeviceAddress);
        var text = RfcommAddress.Format(address);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(ConnectTimeout);

        IRfcommConnection connection;

        try
        {
            connection = await connector.ConnectAsync(text, endpoint.ServiceId, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // The user is still waiting; it was the machine that did not answer.
            throw new TimeoutException($"Timed out reaching {endpoint.DisplayName}.");
        }
        catch (SocketException ex)
        {
            throw new IOException(RfcommErrors.CannotConnect(text, ex), ex);
        }

        return new StreamLink(connection.Stream, remoteName: connection.RemoteName, owner: connection);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerEndpoint>> DiscoverAsync(TimeSpan timeout, CancellationToken cancel = default)
    {
        var budget = timeout <= TimeSpan.Zero ? DefaultScanTimeout : timeout;
        var found = new List<ServerEndpoint>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Never list the machine doing the looking at: Windows reports its own adapter among the
        // devices it can see, and a laptop cannot join its own chat (T4.1).
        var myself = Try(() => finder.LocalAddress());

        if (myself is not null)
        {
            seen.Add(myself);
        }

        // Paired machines first, because they are the ones this machine has been set up with and an
        // inquiry will usually not answer for them at all - a paired machine is normally not
        // discoverable.
        foreach (var device in Try(finder.Remembered, []))
        {
            Add(found, seen, myself, device);
        }

        if (found.Count >= MaxResults)
        {
            return found;
        }

        try
        {
            await foreach (var device in finder.NearbyAsync(budget, cancel).ConfigureAwait(false))
            {
                Add(found, seen, myself, device);

                if (found.Count >= MaxResults)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled scan returns what answered. Throwing here would throw away the one machine
            // that did show up, which is exactly the result the user pressed Stop to look at.
        }
        catch (IOException)
        {
            // No radio, or the stack refused the inquiry. The paired list above is still worth showing.
        }

        return found;
    }

    private static void Add(List<ServerEndpoint> found, HashSet<string> seen, string? myself, RfcommDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.Address) || found.Count >= MaxResults)
        {
            return;
        }

        if (!RfcommAddress.TryParse(device.Address, out var address))
        {
            return;
        }

        var text = RfcommAddress.Format(address);

        if (string.Equals(text, myself, StringComparison.OrdinalIgnoreCase) || !seen.Add(text))
        {
            // Itself, or already listed from the paired set - a machine must appear once, and as the
            // name it was first recognised by.
            return;
        }

        found.Add(new ServerEndpoint(
            string.IsNullOrWhiteSpace(device.Name) ? text : device.Name.Trim(),
            text,
            LanternProtocol.ServiceId));
    }

    /// <summary>
    /// The stack's answers are the least reliable part of this class: a paired list can throw because
    /// there is no radio, and a device name can throw because the stack has not resolved it. Neither is
    /// worth failing a scan over.
    /// </summary>
    private static T Try<T>(Func<T> read, T whenItRefuses)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return whenItRefuses;
        }
    }

    private static string? Try(Func<string?> read) => Try(read, null);
}

/// <summary>
/// The three things this project asks the Bluetooth stack to do, implemented with 32feet.NET. This is
/// the only file in the application that references that library, which is what keeps the option open
/// of replacing it (T0.3, T4.1) without touching the chat.
/// </summary>
public sealed class ThreeFeetRfcommListener : IRfcommListener
{
    private readonly object gate = new();
    private InTheHand.Net.Sockets.BluetoothListener? listener;

    /// <inheritdoc />
    public bool IsActive
    {
        get
        {
            lock (gate)
            {
                return listener?.Active == true;
            }
        }
    }

    /// <inheritdoc />
    public void Start(Guid serviceId, string serviceName)
    {
        var next = new InTheHand.Net.Sockets.BluetoothListener(serviceId)
        {
            ServiceName = serviceName,
        };

        try
        {
            next.Start();
        }
        catch
        {
            next.Dispose();
            throw;
        }

        lock (gate)
        {
            listener = next;
        }
    }

    /// <inheritdoc />
    public async Task<IRfcommConnection?> AcceptAsync(CancellationToken cancel)
    {
        InTheHand.Net.Sockets.BluetoothListener? current;

        lock (gate)
        {
            current = listener;
        }

        if (current is null)
        {
            return null;
        }

        InTheHand.Net.Sockets.BluetoothClient client;

        try
        {
            // No cancellation token in this API: Stop() is what breaks the wait, which is how
            // BtRfcommServer ends an accept loop anyway.
            client = await current.AcceptBluetoothClientAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }

        if (cancel.IsCancellationRequested)
        {
            client.Dispose();
            return null;
        }

        return new ThreeFeetRfcommConnection(client);
    }

    /// <inheritdoc />
    public void Stop()
    {
        InTheHand.Net.Sockets.BluetoothListener? current;

        lock (gate)
        {
            current = listener;
            listener = null;
        }

        if (current is null)
        {
            return;
        }

        try
        {
            current.Stop();
        }
        catch (SocketException)
        {
            // Already gone.
        }
        finally
        {
            current.Dispose();
        }
    }

    public void Dispose() => Stop();
}

/// <inheritdoc cref="IRfcommConnector" />
public sealed class ThreeFeetRfcommConnector : IRfcommConnector
{
    /// <inheritdoc />
    public async Task<IRfcommConnection> ConnectAsync(string deviceAddress, Guid serviceId, CancellationToken cancel)
    {
        var address = RfcommAddress.Parse(deviceAddress);
        var client = new InTheHand.Net.Sockets.BluetoothClient();

        // T4.1 measured that BluetoothClient.ConnectAsync(address, service) in this library version
        // comes straight back with "invalid argument" while the synchronous Connect dials for real and
        // reports a genuine timeout. A connect whose failure cannot be told apart from a bug is worse
        // than a blocking call we own, so: dial on a worker thread, wait on it here, and let the
        // cancellation be a disposal, which is the only thing that interrupts the blocking call.
        var dial = Task.Run(() => client.Connect(new InTheHand.Net.BluetoothEndPoint(address, serviceId, -1)));
        var waiting = Task.Delay(Timeout.InfiniteTimeSpan, cancel);
        var finished = await Task.WhenAny(dial, waiting).ConfigureAwait(false);

        if (finished == dial)
        {
            try
            {
                await dial.ConfigureAwait(false);
            }
            catch
            {
                client.Dispose();
                throw;
            }

            return new ThreeFeetRfcommConnection(client);
        }

        // Nobody will look at the abandoned dial, so look at it here: an unobserved exception on a
        // thread-pool task is a crash waiting for a finaliser.
        _ = dial.ContinueWith(
            abandoned => _ = abandoned.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        client.Dispose();
        cancel.ThrowIfCancellationRequested();
        throw new TimeoutException($"Timed out reaching {deviceAddress}.");
    }
}

/// <inheritdoc cref="IDeviceFinder" />
public sealed class ThreeFeetDeviceFinder : IDeviceFinder
{
    /// <inheritdoc />
    public IReadOnlyList<RfcommDevice> Remembered()
    {
        using var client = new InTheHand.Net.Sockets.BluetoothClient();

        return client.PairedDevices
            .Select(device => new RfcommDevice(RfcommAddress.Format(device.DeviceAddress), Name(device), true))
            .ToArray();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RfcommDevice> NearbyAsync(
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken cancel = default)
    {
        using var client = new InTheHand.Net.Sockets.BluetoothClient
        {
            InquiryLength = timeout,
        };

        await foreach (var device in client.DiscoverDevicesAsync(cancel).ConfigureAwait(false))
        {
            yield return new RfcommDevice(RfcommAddress.Format(device.DeviceAddress), Name(device), false);
        }
    }

    /// <inheritdoc />
    public string? LocalAddress() => TryLocalAddress();

    /// <summary>This machine's own address, or null when the stack will not say.</summary>
    public static string? TryLocalAddress()
    {
        try
        {
#pragma warning disable CA1416 // Windows-only, and this project is Windows-only.
            var radio = InTheHand.Net.Bluetooth.BluetoothRadio.Default;
#pragma warning restore CA1416

            return radio?.LocalAddress is { } address ? RfcommAddress.Format(address) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A name is the one thing the stack sometimes refuses to give for a device it can see - the spike
    /// had to guard this call too.
    /// </summary>
    private static string Name(InTheHand.Net.Sockets.BluetoothDeviceInfo device)
    {
        try
        {
            return device.DeviceName ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}

/// <summary>An open RFCOMM channel, as a <see cref="IRfcommConnection"/>.</summary>
internal sealed class ThreeFeetRfcommConnection : IRfcommConnection
{
    private readonly InTheHand.Net.Sockets.BluetoothClient client;

    internal ThreeFeetRfcommConnection(InTheHand.Net.Sockets.BluetoothClient client) => this.client = client;

    public Stream Stream => client.GetStream();

    public string RemoteName
    {
        get
        {
            try
            {
                return client.RemoteMachineName;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        try
        {
            client.Close();
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        client.Dispose();
    }
}
