namespace Lantern.UI;

/// <summary>
/// The lantern, read once, for every window that shows one.
/// <para>
/// One picture has to serve four places, and they do not ask for the same thing: Explorer and the taskbar jump
/// list want the picture that is <i>inside the executable</i> (which only the build can put there), while a title
/// bar, an alt-tab strip and a taskbar button want the picture that is <i>on the window</i>. The first is the
/// project's <c>ApplicationIcon</c>; this class is the second.
/// </para>
/// <para>
/// The icon is read from a file beside the program rather than embedded in the assembly, for the same reason the
/// two sounds are (F10, see <see cref="Core.SoundNotifier"/>): putting a picture where a person can replace it
/// means changing the picture does not mean changing the code. It also means the file may not be there, which is
/// the state the Windows Forms design surface is always in - it runs somewhere else entirely and has no Media
/// folder of ours to read - so <see cref="Current"/> is allowed to be <see langword="null"/>, and a window told
/// about it keeps the icon the framework would have given it. A window with a missing picture is a window that
/// still opens.
/// </para>
/// <para>
/// The one <see cref="Icon"/> is shared by every window and never disposed. A <c>Form</c> does not own the icon
/// it paints in its corner, so closing a window does not take the picture away from the windows still open - and
/// this class is the only thing that could dispose it, and never does.
/// </para>
/// </summary>
public static class AppIcon
{
    /// <summary>Where the picture sits next to the program, which is where the build puts it.</summary>
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "Media", "icon.ico");

    /// <summary>
    /// The picture, or <see langword="null"/> when this machine was not given one. Read once, because reading it
    /// means opening a file and six windows are all asking for the same one.
    /// </summary>
    public static Icon? Current { get; } = Read();

    /// <summary>
    /// Puts the lantern on a window, and says nothing when there is no lantern to put on it.
    /// </summary>
    /// <remarks>
    /// Called from every window's constructor rather than left to the framework. Left to itself a window shows
    /// whatever icon the executable carries, which is the same picture and would look the same - but a window that
    /// was never told what it shows is a window no test can ask, and the difference between those two states is
    /// how "the taskbar icon is wrong" turns into an afternoon.
    /// </remarks>
    public static void Apply(Form form)
    {
        if (Current is { } icon)
        {
            form.Icon = icon;
        }
    }

    private static Icon? Read()
    {
        try
        {
            return File.Exists(FilePath) ? new Icon(FilePath) : null;
        }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException)
        {
            // An icon is the least important thing about a window, and there is nobody standing in front of this
            // machine at the moment it is read to be told about it: the design surface reads it with no Media
            // folder to find, and a person's desktop has one more window than a test run does. So a picture that
            // cannot be read is a picture that is not shown, and the window opens anyway.
            _ = e;

            return null;
        }
    }
}
