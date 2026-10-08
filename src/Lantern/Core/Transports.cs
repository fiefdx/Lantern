using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// Which transport a run of Lantern was told to use. See <see cref="Transports.Wanted"/>.
/// </summary>
public enum TransportWanted
{
    /// <summary>The radio if there is one, loopback if there is not, and the status bar says which.</summary>
    Auto,

    /// <summary>The radio, and hear about it if there isn't one.</summary>
    Bluetooth,

    /// <summary>This machine alone: how two copies of Lantern are driven through a checklist on one laptop.</summary>
    Loopback,
}

/// <summary>
/// Builds the transports this process will use, and says what they are in one line.
/// </summary>
/// <remarks>
/// The startup window is the only place that decides whether a chat runs over a radio or over loopback,
/// and it decides once. Everything below it — <see cref="ChatServer"/>, <see cref="ChatClient"/>, the
/// frame codec — is told an <see cref="IBtServer"/> or <see cref="IBtClient"/> and cannot tell the
/// difference, which is what makes the two of them the same program.
/// </remarks>
public interface ITransportFactory
{
    /// <summary>One word, for the status bar: "Bluetooth" or "loopback".</summary>
    string Name { get; }

    /// <summary>
    /// A sentence saying what this transport can reach, shown before anyone clicks anything. Saying
    /// "loopback" out loud matters: it means no other machine will ever appear in the list.
    /// </summary>
    string Description { get; }

    /// <summary>A fresh transport for one chat being hosted.</summary>
    IBtServer NewServer(string serverName);

    /// <summary>A fresh transport for one connection or one scan.</summary>
    IBtClient NewClient();
}

/// <summary>Real RFCOMM between laptops: the only transport that is also the product.</summary>
public sealed class BluetoothTransportFactory : ITransportFactory
{
    private readonly string? localAddress;

    /// <param name="localAddress">
    /// This machine's own address, if it was found: it is what the server advertises as its endpoint and
    /// what a scan uses to avoid listing the machine doing the looking. Null when it could not be read,
    /// which leaves both jobs working, just less informatively.
    /// </param>
    public BluetoothTransportFactory(string? localAddress = null) => this.localAddress = localAddress;

    /// <inheritdoc />
    public string Name => "Bluetooth";

    /// <inheritdoc />
    public string Description => localAddress is null
        ? "over Bluetooth"
        : $"over Bluetooth, as {localAddress}";

    /// <inheritdoc />
    public IBtServer NewServer(string serverName) => new BtRfcommServer(serverName);

    /// <inheritdoc />
    public IBtClient NewClient() => new BtRfcommClient();
}

/// <summary>
/// Loopback TCP on this machine. Permanent, and not a shortcut: one machine cannot be both ends of a
/// Bluetooth link, there is no API for choosing between two radios, and a test suite that needed two
/// laptops and a human would not get run (T4.1 D1).
/// </summary>
public sealed class LoopbackTransportFactory : ITransportFactory
{
    /// <param name="why">How this transport came to be chosen, for the status bar.</param>
    public LoopbackTransportFactory(string? why = null) => Description = why ?? "over loopback, on this machine alone";

    /// <inheritdoc />
    public string Name => "loopback";

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public IBtServer NewServer(string serverName) => new BtLoopbackServer(serverName);

    /// <inheritdoc />
    public IBtClient NewClient() => new BtLoopbackClient();
}

/// <summary>
/// The one decision about which transport a run uses, and the one place that reads the environment for it.
/// </summary>
public static class Transports
{
    /// <summary>Set this to <c>loopback</c> or <c>bluetooth</c> to decide it yourself.</summary>
    public const string Variable = "LANTERN_TRANSPORT";

    /// <summary>Same thing for people who would rather not set an environment variable.</summary>
    public const string LoopbackSwitch = "--loopback";

    /// <summary>
    /// What this run was told to use, when it was told anything at all. Three answers because there are
    /// three things a person can mean, and "no" cannot tell "I don't know" from "don't you dare".
    /// </summary>
    public static TransportWanted Wanted(string[]? commandLine, Func<string, string?>? getenv = null)
    {
        // The switch wins over the environment variable: whoever typed it is in front of this particular
        // window, and a variable left set an hour ago should not overrule them.
        if (commandLine?.Any(a => string.Equals(a, LoopbackSwitch, StringComparison.OrdinalIgnoreCase)) ?? false)
        {
            return TransportWanted.Loopback;
        }

        getenv ??= Environment.GetEnvironmentVariable;

        var setting = getenv(Variable)?.Trim();

        if (string.Equals(setting, "loopback", StringComparison.OrdinalIgnoreCase))
        {
            return TransportWanted.Loopback;
        }

        if (string.Equals(setting, "bluetooth", StringComparison.OrdinalIgnoreCase))
        {
            return TransportWanted.Bluetooth;
        }

        return TransportWanted.Auto;
    }

    /// <summary>
    /// The transport for a run of the program: the radio when there is one to use, loopback when there is
    /// not, and whatever a person asked for when a person asked for something.
    /// </summary>
    /// <remarks>
    /// Falling back on its own is the right default for a program that has to open a window on any laptop,
    /// but it says so in the status bar instead of leaving somebody to work out why nothing is ever found.
    /// Insisting on the radio is not fallen back from: that machine should be told its radio is the
    /// problem, which the failure to host or join will do in its own words.
    /// </remarks>
    public static ITransportFactory ForStartup(
        TransportWanted wanted = TransportWanted.Auto,
        Func<string?>? probeRadio = null)
    {
        if (wanted == TransportWanted.Loopback)
        {
            return new LoopbackTransportFactory(
                $"over loopback, on this machine alone ({LoopbackSwitch}, or {Variable}=loopback)");
        }

        probeRadio ??= RadioAddress;

        string? address;

        try
        {
            address = probeRadio();
        }
        catch (Exception)
        {
            // A driver that will not answer is the same news as a driver that is not there, and the
            // window opens either way.
            address = null;
        }

        if (address is not null)
        {
            return new BluetoothTransportFactory(address);
        }

        return wanted == TransportWanted.Bluetooth
            ? new BluetoothTransportFactory(null)
            : new LoopbackTransportFactory(
                $"over loopback, because no Bluetooth radio was found (set {Variable}=bluetooth to insist)");
    }

    /// <summary>This machine's Bluetooth address, or null when there is no radio or nothing to read.</summary>
    public static string? RadioAddress()
    {
        try
        {
            return new ThreeFeetDeviceFinder().LocalAddress();
        }
        catch (Exception)
        {
            // No radio, no platform support, a driver that will not answer: all the same answer here.
            // The window still opens, on loopback.
            return null;
        }
    }
}
