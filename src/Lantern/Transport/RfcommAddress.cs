using System.Diagnostics.CodeAnalysis;
using InTheHand.Net;

namespace Lantern.Transport;

/// <summary>
/// Bluetooth device addresses as text, which is the only form in which one travels through this
/// codebase: it is what a host shows on its own screen, what a user types, and what a
/// <see cref="ServerEndpoint"/> holds.
/// </summary>
/// <remarks>
/// The stack's own parser is picky about separators, and a mistyped address is the most common way for
/// a field test to fail (docs/testing/bluetooth-pairing-how-to.md), so Lantern accepts the shapes people
/// actually write and says plainly when none of them fits — rather than passing a bare
/// <c>FormatException</c> to a window.
/// </remarks>
public static class RfcommAddress
{
    /// <summary>A Bluetooth address is 48 bits, written as twelve hex digits.</summary>
    public const int HexDigits = 12;

    // BluetoothAddress is a class in 32feet.NET, not a struct, so "no address" is genuinely null here
    // and the nullable annotations have to say so.


    /// <summary>The address in the form Lantern displays and stores: <c>F8E4E3F9603B</c>.</summary>
    public static string Format(BluetoothAddress address) => address.ToString().ToUpperInvariant();

    /// <summary>
    /// Whether <paramref name="text"/> is a Bluetooth address rather than, say, a
    /// <c>host:port</c> for the loopback transport. Grouped forms are accepted —
    /// <c>F8-E4-E3-F9-60-3B</c>, <c>f8:e4:…</c> and <c>F8 E4 …</c> — but a grouped form must be fully
    /// grouped, because "127.0.0.1:43797" contains twelve-ish digits too and belongs to TCP.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out BluetoothAddress? address)
    {
        address = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        if (trimmed.Contains('.', StringComparison.Ordinal))
        {
            // An IPv4 address, or a dotted host name: never Bluetooth.
            return false;
        }

        if (trimmed.Any(c => !Uri.IsHexDigit(c) && !IsSeparator(c)))
        {
            // Junk in the middle is not a typo to be cleaned up - "F8E4E3F9603BZZ" is a different
            // machine, or not a machine, and quietly ignoring the ZZ would send somebody to the wrong
            // laptop.
            return false;
        }

        var separators = trimmed.Where(IsSeparator).ToArray();

        if (separators.Length > 0)
        {
            var groups = trimmed.Split(separators.Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries);
            if (groups.Length != HexDigits / 2)
            {
                return false;
            }
        }

        var digits = new string(trimmed.Where(Uri.IsHexDigit).ToArray());

        return digits.Length == HexDigits && BluetoothAddress.TryParse(digits, out address);
    }

    /// <summary>The address, or a message a window can show as it stands.</summary>
    public static BluetoothAddress Parse(string text)
        => TryParse(text, out var address)
            ? address
            : throw new FormatException(
                $"\"{text}\" is not a Bluetooth address. It is twelve hexadecimal digits, like F8E4E3F9603B (or F8-E4-E3-F9-60-3B).");

    private static bool IsSeparator(char c) => c is '-' or ':' or ' ';
}
