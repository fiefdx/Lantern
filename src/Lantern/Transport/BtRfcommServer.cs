using Lantern.Core;

namespace Lantern.Transport;

/// <summary>
/// The host side over real Bluetooth (T4.2): publish the Lantern service, accept channels, hand each one
/// up as an <see cref="IClientLink"/>.
/// </summary>
/// <remarks>
/// Structurally the same as <see cref="BtLoopbackServer"/>, and that is the point:
/// <see cref="ChatServer"/> drives both through <see cref="IBtServer"/> and every test written against
/// the loopback one therefore describes this one too. The differences are all Bluetooth's:
/// there is no port to pick (the service GUID is the address), the service registration has to be
/// removed again or Windows keeps advertising a host that has gone, and a machine that has been hosting
/// cannot host again until that is done — which is why <see cref="Stop"/> is careful.
/// </remarks>
public sealed class BtRfcommServer : IBtServer
{
    /// <summary>
    /// The name the service is published under. It is the only human-readable thing about a private
    /// service GUID, and it is what the Windows Bluetooth panel shows when something has gone wrong
    /// (T4.1 D4).
    /// </summary>
    public const string ServiceName = "Lantern";

    /// <summary>
    /// How long <see cref="Stop"/> will wait for the radio to finish saying goodbye. One second: long enough
    /// for a normal close of a machine that is still switched on, short enough that a laptop in a bag is not
    /// allowed to hold a window open. The closing is not cancelled by walking away, only waited on less.
    /// </summary>
    public static readonly TimeSpan TearDownGrace = TimeSpan.FromSeconds(1);

    private readonly IRfcommListener listener;
    private readonly Func<string?> localAddress;
    private readonly List<StreamLink> links = [];
    private readonly object gate = new();
    private CancellationTokenSource? acceptCancellation;

    /// <param name="serverName">announced to clients in HELLO_ACK; the machine name when not given</param>
    /// <param name="listener">the radio, or a stand-in for it in tests</param>
    /// <param name="localAddress">how this machine's own address is found</param>
    public BtRfcommServer(string? serverName = null, IRfcommListener? listener = null, Func<string?>? localAddress = null)
    {
        ServerName = string.IsNullOrWhiteSpace(serverName) ? Environment.MachineName : serverName.Trim();
        this.listener = listener ?? new ThreeFeetRfcommListener();
        this.localAddress = localAddress ?? ThreeFeetDeviceFinder.TryLocalAddress;
    }

    /// <summary>Name announced to clients.</summary>
    public string ServerName { get; }

    /// <summary>The service GUID handed to <see cref="StartAsync"/>.</summary>
    public Guid ServiceId { get; private set; }

    /// <inheritdoc />
    public bool IsListening { get; private set; }

    /// <summary>
    /// This machine's Bluetooth address, which the host window shows so that somebody at the other
    /// laptop can type it in. Null when the stack will not say.
    /// </summary>
    public string? LocalAddress { get; private set; }

    /// <inheritdoc />
    public ServerEndpoint Endpoint => IsListening
        ? new ServerEndpoint(ServerName, LocalAddress ?? "address unknown", ServiceId)
        : throw new InvalidOperationException("The server is not listening yet, so it has no address.");

    /// <inheritdoc />
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
            throw new ObjectDisposedException(nameof(BtRfcommServer));
        }

        lock (gate)
        {
            if (IsListening)
            {
                return Task.CompletedTask;
            }

            ServiceId = serviceId;
            acceptCancellation = new CancellationTokenSource();
        }

        try
        {
            listener.Start(serviceId, ServiceName);
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                acceptCancellation?.Dispose();
                acceptCancellation = null;
                ServiceId = Guid.Empty;
            }

            // The registration is the thing that fails when the radio is off or the service is already
            // taken, and both cases need more than the stack's message to be fixable.
            throw new IOException(RfcommErrors.CannotListen(ex), ex);
        }

        LocalAddress = LocalAddressOrNull();

        lock (gate)
        {
            IsListening = listener.IsActive;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IClientLink?> AcceptAsync(CancellationToken cancel = default)
    {
        CancellationTokenSource? source;

        lock (gate)
        {
            source = acceptCancellation;

            if (!IsListening || source is null)
            {
                return null;
            }
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, source.Token);

        IRfcommConnection? connection;

        try
        {
            connection = await listener.AcceptAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            // The radio or the listener went away under us. ChatServer stops accepting when this
            // returns null, which is honest; it is not this layer's job to explain a broken radio.
            return null;
        }

        if (connection is null)
        {
            return null;
        }

        var link = new StreamLink(connection.Stream, remoteName: connection.RemoteName, owner: connection);

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
        StreamLink[] open;

        lock (gate)
        {
            if (!IsListening && acceptCancellation is null)
            {
                return;
            }

            IsListening = false;
            source = acceptCancellation;
            open = links.ToArray();
            links.Clear();
            acceptCancellation = null;
        }

        source?.Cancel();

        // Closing a Bluetooth channel is the part of quitting that takes time: the stack wants to finish a
        // polite goodbye with a machine that may be switched off, and it is quite capable of spending seconds
        // doing it. So the goodbye is started, everything at once, and waited on only so long - the caller
        // (ChatServer.StopAsync, and behind it a window somebody closed) is not the place to spend the radio's
        // patience. Whatever is still closing carries on by itself and releases its handles when it gets there.
        var packingUp = new List<Task>(open.Length + 1)
        {
            Task.Run(() =>
            {
                try
                {
                    // Removing the SDP record matters as much as closing the socket: a machine that still
                    // advertises Lantern after its host quit is a machine whose next visitor connects to
                    // nothing, and the fault then looks like it belongs to the visitor.
                    listener.Stop();
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }),
        };

        // Scenario 6: quitting the host drops every client, which is what makes their "connection lost"
        // path run. All of them at once: three slow clients are not three seconds of somebody's window.
        packingUp.AddRange(open.Select(link => Task.Run(() =>
        {
            try
            {
                link.Close();
            }
            catch (Exception)
            {
                // A channel that complains on the way out is a channel that is closing.
            }
        })));

        try
        {
            Task.WaitAll(packingUp.ToArray(), TearDownGrace);
        }
        catch (Exception)
        {
            // Faults are handled inside each one; WaitAll only rethrows them, and there is nobody to tell.
        }

        source?.Dispose();
    }

    private string? LocalAddressOrNull()
    {
        try
        {
            return localAddress();
        }
        catch (Exception)
        {
            // Not knowing one's own address is an inconvenience — the host cannot tell anyone what to
            // type — and not a reason a chat cannot start.
            return null;
        }
    }

    private bool disposed;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        disposed = true;
        Stop();
        listener.Dispose();
        return ValueTask.CompletedTask;
    }
}
