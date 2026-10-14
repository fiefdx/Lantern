using System.Runtime.InteropServices;
using System.Windows.Forms;
using Lantern.Core;
using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// The lantern on the window. One picture has to serve four places at once: the Explorer listing of the
/// executable, the taskbar button, the corner of every title bar, and the alt-tab strip - and each asks for a
/// different size. So the art is not simply handed over at its own 1254 pixels: it has to arrive as a set of
/// pictures, one for each size something will ask for, and it has to be the transparent art rather than the
/// square version of it, because a title bar that shows a filled square reads as a window with a hole punched
/// in the theme.
/// </summary>
public sealed class AppIconTests
{
    private const uint LRDefaultColor = 0x00000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int PrivateExtractIcons(
        string lpszFile, int nIconIndex, int cx, int cy, IntPtr[]? phicon, int[]? piconid, int nIcons, uint fuFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Every window the program can open, wearing the picture it was given.
    /// </summary>
    [Fact]
    public void EveryWindowOfTheProgramWearsTheLantern()
    {
        Sta.Run(() =>
        {
            foreach (var window in Openable())
            {
                var because = $"{window.GetType().Name}";

                var icon = Assert.IsAssignableFrom<Icon>(window.Icon);

                Assert.NotNull(icon);

                using var art = icon.ToBitmap();
                LooksLikeTheLantern(art, because);
            }
        });
    }

    /// <summary>
    /// The icon is a set of pictures rather than one big one, because Windows asks for a size and takes the
    /// closest thing it is offered. Offer it only a 1254-pixel picture and the 16-pixel corner of a title bar
    /// gets that picture shrunk by whoever happens to be asked, which is how a lantern becomes a smear.
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void TheIconCarriesASizeForEveryPlaceItIsShown(int asks)
    {
        var extracted = Extract(Exe, asks, asks);

        try
        {
            Assert.True(extracted.Length > 0, $"Windows would not give the program's icon at {asks} pixels at all");

            using var icon = Icon.FromHandle(extracted[0]);
            using var art = icon.ToBitmap();

            Assert.Equal(asks, art.Width);
            Assert.Equal(asks, art.Height);

            Assert.True(
                art.Width * art.Height * 0.10 < DistinctColours(art),
                $"the {asks}-pixel picture has {DistinctColours(art)} colours in it, which is a blank rather than a picture");
        }
        finally
        {
            foreach (var handle in extracted)
            {
                DestroyIcon(handle);
            }
        }
    }

    /// <summary>
    /// The executable itself carries the picture, which is what the taskbar jump list, the Explorer listing and
    /// a window that was never told otherwise all show. A title bar can be fixed from the code; the file on disk
    /// can only be fixed at build time.
    /// </summary>
    [Fact]
    public void TheProgramItselfCarriesTheLantern()
    {
        Assert.True(File.Exists(Exe), $"no executable next to the tests to look an icon for");

        var extracted = Extract(Exe, 32, 32);

        try
        {
            Assert.True(extracted.Length > 0, "the executable offers no icon at 32 pixels");

            using var icon = Icon.FromHandle(extracted[0]);
            using var art = icon.ToBitmap();

            LooksLikeTheLantern(art, "Lantern.exe");
        }
        finally
        {
            foreach (var handle in extracted)
            {
                DestroyIcon(handle);
            }
        }
    }

    /// <summary>
    /// The picture also arrives beside the program, which is how a window is given its icon without a rebuild,
    /// and how the icon can be looked at while a test is deciding whether it is the right one.
    /// </summary>
    [Fact]
    public void TheLanternArrivesNextToTheProgram()
    {
        Assert.True(File.Exists(AppIcon.FilePath), $"nothing at {AppIcon.FilePath}");

        Assert.NotNull(AppIcon.Current);

        using var art = AppIcon.Current!.ToBitmap();

        // The frame a title bar wants, not the 1254-pixel original. A multiframe icon reports its first frame,
        // and it is worth knowing which one that is when the complaint is that the small one is a smear.
        Assert.True(art.Width <= 64, $"the icon hands over a {art.Width}-pixel picture to a title bar");

        LooksLikeTheLantern(art, "the icon file");
    }

    /// <summary>
    /// Two things make this picture itself rather than any picture: the paper around it is transparent, and
    /// there is a warm flame in the middle of it. The opaque version of the art fails the first of these, which
    /// is why the transparent one is what ships.
    /// </summary>
    private static void LooksLikeTheLantern(Bitmap art, string because)
    {
        var pixels = art.Width * art.Height;

        var clear = 0;
        var flame = 0;

        for (var y = 0; y < art.Height; y++)
        {
            for (var x = 0; x < art.Width; x++)
            {
                var p = art.GetPixel(x, y);

                if (p.A < 40)
                {
                    clear++;
                }
                else if (p.R > 200 && p.G > 120 && p.B < 120)
                {
                    flame++;
                }
            }
        }

        Assert.True(
            pixels * 0.15 < clear,
            $"{because} is {100.0 * clear / pixels:0}% empty where the lantern art is about half transparent - this is a square of background, not the transparent art");

        Assert.True(
            flame > 3,
            $"{because} has no warm flame in it ({flame} flame-coloured pixels), so it is not the lantern");
    }

    private static int DistinctColours(Bitmap art)
    {
        var seen = new HashSet<int>();

        for (var y = 0; y < art.Height; y++)
        {
            for (var x = 0; x < art.Width; x++)
            {
                seen.Add(art.GetPixel(x, y).ToArgb());
            }
        }

        return seen.Count;
    }

    private static string Exe => Path.Combine(AppContext.BaseDirectory, "Lantern.exe");

    private static IntPtr[] Extract(string file, int wide, int high)
    {
        var handles = new IntPtr[1];

        PrivateExtractIcons(file, 0, wide, high, handles, null, 1, LRDefaultColor);

        return handles[0] == IntPtr.Zero ? [] : handles;
    }

    /// <summary>
    /// Every window the program can be asked to open, from the first one a person sees to the last dialog.
    /// </summary>
    /// <remarks>
    /// <see cref="ConfirmDialog"/> is not among them because its only constructor is the design surface's, and
    /// the program refuses to build one without a question to ask (see <see cref="DesignViewTests"/>). It is a
    /// modal dialog owned by a window that does wear the icon, which is where it gets its own from.
    /// </remarks>
    private static IEnumerable<Form> Openable()
    {
        yield return new StartupForm();

        yield return new HostForm(new FakeChatService("Haitao"));

        yield return new ClientForm(new FakeChatService("Haitao"));

        yield return new NewRoomDialog();

        yield return new EmojiPickerForm();

        yield return new PictureViewerForm(
            new PictureMessage("User 2", "Room 1", "sunset.png", new byte[] { 1, 2, 3, 4 }));
    }
}
