namespace Lantern.UI;

/// <summary>
/// A table of faces, so that choosing an emoji does not depend on the operating system.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this window exists.</b> The report was "the Win + . menu does not pop up". Nothing in Lantern can stop
/// that keystroke - the shell takes Win-key combinations before an application is told anything, and this window's
/// only key handlers are Ctrl+N, Ctrl+D and Enter - and the machine checked out: text-input service running, the two
/// processes that draw the panel running, no policy disabling the hotkey, the app not elevated. Which left a feature
/// of somebody else's program standing between a person and the thing they wanted to send, with nothing Lantern could
/// promise about it. Over a remote desktop the shell keeps Win + . for the local session entirely, and a chat app
/// used across a room is exactly where that happens.
/// </para>
/// <para>
/// So Lantern asks for nothing: it draws its own table. The tiles are the same colour faces the transcript gets
/// (<see cref="ColorEmoji"/>), which makes the picker a proof of the renderer as well as an input method - what you
/// click is what arrives.
/// </para>
/// <para>
/// <b>One entry per text element</b>, and the table says so in its own test: a joined family and a flag are each one
/// tile, the same thing <see cref="Domain.ValidationRules.Length"/> counts them as. Whatever the table offers, the
/// chat window receives as one character by that ruler.
/// </para>
/// </remarks>
public partial class EmojiPickerForm : Form
{
    /// <summary>
    /// What is on offer, written as escapes rather than as pictures in the source: a table of emoji in a source file
    /// is unreadable to the person who has to review it, and invisible to a diff.
    /// </summary>
    /// <remarks>
    /// Every entry is <b>one code point</b>, or one code point and the variation selector that asks for the picture
    /// rather than the monochrome one. That is not fussiness about counting: the faces on these tiles are drawn here,
    /// out of the font's colour tables, one code point at a time, and a face that is really several code points put
    /// together - a family joined by zero-width joiners, a flag made of two regional letters - arrives as several
    /// strangers rather than as one face. The joined family and the flag of Vietnam were on this table until somebody
    /// looked at the last row; they are still understood by <see cref="ColorEmoji.IsEmoji"/> and by the transcript,
    /// because a message from another machine may contain them, and understanding is not the same as offering.
    /// </remarks>
    public static readonly IReadOnlyList<string> Elements =
    [
        "\U0001F600", "\U0001F603", "\U0001F609", "\U0001F60D", "\U0001F970", "\U0001F618", "\U0001F60E", "\U0001F914",
        "\U0001F605", "\U0001F923", "\U0001F62D", "\U0001F621", "\U0001F631", "\U0001F97A", "\U0001F917", "\U0001F634",
        "\U0001F44D", "\U0001F44E", "\U0001F44C", "\U0001F64F", "\U0001F44F", "\U0001F4AA", "\u270C\uFE0F", "\U0001F91D",
        "\u2764\uFE0F", "\U0001F494", "\U0001F496", "\U0001F4AF", "\U0001F525", "\u2B50", "\U0001F389", "\u2728",
        "\U0001F355", "\u2615", "\U0001F37A", "\U0001F382", "\U0001F436", "\U0001F431", "\U0001F327\uFE0F", "\u26BD",
        "\U0001F697", "\u2708\uFE0F", "\U0001F3E0", "\U0001F4BB", "\U0001F4F1", "\U0001F3B5",
    ];

    /// <summary>Raised with the face that was chosen, just before this window closes.</summary>
    public event Action<string>? Picked;

    private readonly List<Button> tiles = [];

    /// <summary>The tiles, in the order they were laid out: the tests look at these rather than at pixels.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<Button> Tiles => tiles;

    /// <summary>
    /// The shape the design host needs: a public constructor that draws the window with nothing asked of it. It does
    /// the work itself rather than chaining, because the host constructs the class to paint it and a chained
    /// constructor is the shape that went wrong once before, in v1.0.4.
    /// </summary>
    public EmojiPickerForm()
    {
        InitializeComponent();
        BuildTiles();
    }

    /// <summary>Opens the table owned by the window that asked, so it stays above it and goes away with it.</summary>
    public EmojiPickerForm(Form? owner)
        : this()
    {
        if (owner is not null)
        {
            Owner = owner;
        }
    }

    /// <summary>
    /// Puts the table under the button that opened it, inside the owner's screen, so that it reads as belonging to
    /// the message box rather than as a dialog that has wandered in.
    /// </summary>
    public void PlaceUnder(Control anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        var at = anchor.PointToScreen(new Point(0, anchor.Height));
        var screen = Screen.FromControl(anchor).WorkingArea;

        Location = new Point(
            Math.Max(screen.Left, Math.Min(at.X - Width + anchor.Width, screen.Right - Width)),
            Math.Max(screen.Top, Math.Min(at.Y, screen.Bottom - Height)));
    }

    private void BuildTiles()
    {
        var index = 0;

        foreach (var element in Elements)
        {
            var tile = new Button
            {
                Name = $"tile{index}",
                Margin = new Padding(1),
                FlatStyle = FlatStyle.Flat,
                TabStop = true,
                AccessibleName = $"emoji {element}",
                Tag = element,
            };

            tile.FlatAppearance.BorderColor = Color.FromArgb(221, 221, 221);

            // A face that cannot be drawn still gets a tile, showing what it really is: the characters themselves.
            // A blank square would look like a hole in the table and be reported as one.
            tile.Image = ColorEmoji.Glyph(element, 22);
            tile.Text = tile.Image is null ? element : string.Empty;

            toolTip.SetToolTip(tile, element);

            var chosen = element;
            tile.Click += (_, _) => Pick(chosen);

            grid.Controls.Add(tile, index % 8, index / 8);
            tiles.Add(tile);
            index++;
        }
    }

    private void Pick(string element)
    {
        Picked?.Invoke(element);
        Close();
    }
}
