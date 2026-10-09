using System.ComponentModel;
using Lantern.Core;
using Lantern.Storage;

namespace Lantern.UI;

/// <summary>
/// The one door between the windows and the Visual Studio design surface.
/// <para>
/// Every window here is built with the thing it shows - a chat, a question, a radio - because a window
/// missing one is a window whose every button does nothing. That is also exactly what the Windows Forms
/// designer cannot do: to draw a form it constructs that form, and it has no chat to hand over. So each
/// window carries a constructor with no arguments for the designer's use, and every one of them comes
/// through here first, where being drawn and being used are told apart.
/// </para>
/// <para>
/// The rule this file exists to keep: <b>the designer may open a window on nothing; the program may not.</b>
/// A window that opens without its chat would be a window somebody clicks, and a program that refuses to
/// build such a window says so at the constructor rather than at the button.
/// </para>
/// </summary>
internal static class DesignTime
{
    /// <summary>Whose name the stand-in chat goes by, and the only nickname a design surface ever shows.</summary>
    internal const string Nickname = "Lantern";

    /// <summary>
    /// Whether Lantern's types are being built by the Windows Forms design host rather than by Lantern itself.
    /// </summary>
    /// <remarks>
    /// Two probes, and either is enough. <see cref="LicenseManager.UsageMode"/> is the one the framework
    /// offers and it knows nothing about this project; the other knows nothing about design at all, which is
    /// why it can be trusted: the design host is a separate program sitting next to Visual Studio's Windows
    /// Forms components, so the name of the running executable says who asked for the window. Between them,
    /// a window being drawn and a window being used are not confused for each other.
    /// </remarks>
    internal static bool IsHosting()
    {
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
        {
            return true;
        }

        var program = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);

        return program.StartsWith("DesignToolsServer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(program, "devenv", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The chat a design surface shows: the in-memory one, so the window has a room list, a member list and
    /// a transcript to lay itself around. Asked for anywhere else, this is the moment a window would be
    /// built without a chat, and it is refused instead.
    /// </summary>
    internal static IChatService ChatService(bool isHost, string window)
        => IsHosting()
            ? new FakeChatService(Nickname, isHost)
            : throw new InvalidOperationException(
                $"{window} belongs to a chat, which has to be handed to it. Only the design surface builds one without.");

    /// <summary>
    /// The words a confirmation shows while it is being designed. A confirmation that has not been given
    /// its question is not a confirmation waiting to be filled in - it is no confirmation at all - so
    /// anywhere outside the designer this is a refusal, and a pointer to the methods that do have it.
    /// </summary>
    internal static (string Title, string Message) Confirmation(string window)
        => IsHosting()
            ? ("Confirm", "Are you sure?")
            : throw new InvalidOperationException(
                $"{window} is shown with its own question. Ask ConfirmQuit, ConfirmDeleteRoom or ConfirmLeaveRoom rather than building it.");

    /// <summary>
    /// The transport the start window is drawn over. While it is being drawn that is loopback: the design
    /// host must not enumerate a radio, and a window that reaches for the Bluetooth stack the moment it is
    /// constructed is a window that makes the designer wait for a driver.
    /// </summary>
    internal static ITransportFactory Transport()
        => IsHosting()
            ? Transports.ForStartup(TransportWanted.Loopback)
            : Transports.ForStartup(Transports.Wanted(Environment.GetCommandLineArgs()));

    /// <summary>
    /// The laptop's own chat history, or none while the window is being drawn. The designer has no business
    /// reading a file out of the user's app data, and a list filled with somebody's real chats is a poor way
    /// to judge a column width.
    /// </summary>
    internal static IKnownHosts? KnownHosts() => IsHosting() ? null : new JsonKnownHostsStore();
}
