using System.Net.Sockets;

namespace Lantern.Transport;

/// <summary>
/// What a Bluetooth failure is told as, in a status bar.
/// </summary>
/// <remarks>
/// The socket code is the only part of a Bluetooth failure that means anything on its own, and the
/// stack's own message is either in a language the reader does not read or says "invalid argument" about
/// a machine that is simply switched off. The three codes below are the ones measured in T4.1 against
/// real devices; the rest is passed through in the stack's words, because inventing an explanation for a
/// failure nobody has seen is how a defect report ends up useless.
/// </remarks>
public static class RfcommErrors
{
    /// <summary>A sentence fragment describing why the connect failed, starting lower-case.</summary>
    public static string Describe(Exception error) => error switch
    {
        SocketException socket => Describe(socket),
        _ => error.Message,
    };

    /// <summary>The same, for the one exception type that carries a code worth naming.</summary>
    public static string Describe(SocketException error) => error.SocketErrorCode switch
    {
        SocketError.TimedOut =>
            "it did not answer. Is that machine switched on, awake, in range, and hosting Lantern?",
        SocketError.AddressNotAvailable =>
            "this machine cannot dial that address. Check it against the address the other machine says about itself.",
        SocketError.ConnectionRefused =>
            "refused the connection: it is reachable, but not hosting Lantern at the moment.",
        SocketError.HostNotFound =>
            "is not known to this machine. Pair the two machines first (docs/testing/bluetooth-pairing-how-to.md).",
        _ => error.Message,
    };

    /// <summary>What to tell a window when a join attempt failed.</summary>
    public static string CannotConnect(string address, Exception error)
        => $"Could not reach {address}: {Describe(error)}";

    /// <summary>
    /// What to tell a window when the host could not start. This one is worth being blunt about: it
    /// usually means another Lantern is still listening on this machine, and a user who is not told that
    /// will look for the bug somewhere else.
    /// </summary>
    public static string CannotListen(Exception error)
        => $"Could not start hosting over Bluetooth: {error.Message}. "
           + "Another Lantern on this machine may still be hosting, or the radio is off.";
}
