namespace Lantern.Transport;

/// <summary>
/// An <see cref="IClientLink"/> over two streams, which is what an RFCOMM channel is once somebody has
/// opened one (T4.2).
/// </summary>
/// <remarks>
/// Deliberately general: the read side and the write side are the same duplex stream in practice, and
/// nothing here knows whether it came from a Bluetooth radio, a socket or a pair of memory streams in a
/// test. That last case is the reason this class is separate from
/// <see cref="ThreeFeetRfcommListener"/>: the link's promises — closing twice is fine, <c>Closed</c>
/// fires once, the owner is released — have to be tested, and a radio cannot be.
/// </remarks>
public sealed class StreamLink : IClientLink
{
    private readonly Stream input;
    private readonly Stream? output;
    private readonly IDisposable? owner;
    private readonly object gate = new();
    private bool closed;

    /// <param name="input">read side</param>
    /// <param name="output">write side; the same object for a duplex stream</param>
    /// <param name="remoteName">who is on the other end, for the member list and the status bar</param>
    /// <param name="connectionId">session key on the server side; generated when not given</param>
    /// <param name="owner">
    /// what made this stream — the connected client, in the RFCOMM case — disposed along with the link,
    /// so that closing a link cannot leave a radio handle alive.
    /// </param>
    public StreamLink(
        Stream input,
        Stream? output = null,
        string? remoteName = null,
        string? connectionId = null,
        IDisposable? owner = null)
    {
        this.input = input ?? throw new ArgumentNullException(nameof(input));
        this.output = output;
        this.owner = owner;
        RemoteName = string.IsNullOrWhiteSpace(remoteName) ? "unknown" : remoteName;
        ConnectionId = connectionId ?? Guid.NewGuid().ToString("N");
    }

    /// <inheritdoc />
    public Stream Input => input;

    /// <inheritdoc />
    public Stream Output => output ?? input;

    /// <inheritdoc />
    public string RemoteName { get; }

    /// <inheritdoc />
    public string ConnectionId { get; }

    /// <inheritdoc />
    public event EventHandler<Exception?>? Closed;

    /// <inheritdoc />
    public void Close()
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            closed = true;
        }

        // Nobody is told a reason: closing a link is either the user leaving or the host quitting, and
        // both look the same from here. A stream that stops mid-frame is noticed by the reader, which
        // is where a real failure is described.
        QuietlyDispose(owner);
        QuietlyDispose(input);

        if (output is not null && !ReferenceEquals(output, input))
        {
            QuietlyDispose(output);
        }

        Closed?.Invoke(this, null);
    }

    public void Dispose() => Close();

    private static void QuietlyDispose(IDisposable? thing)
    {
        try
        {
            thing?.Dispose();
        }
        catch (IOException)
        {
            // A channel with unread bytes in it complains when it is torn down. That is what the other
            // end hanging up looks like, and it is not a reason to bother anybody.
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
