using Lantern.Domain;

namespace Lantern.UI;

/// <summary>
/// The strip above the message box: what its tools look like when they are on, and what they have to offer.
/// <para>
/// The message box is a text editor now, so it has a row of tools, and a row of tools is full of decisions that
/// have nothing to do with chat: which colours are on offer, in what order the sizes come round, what shade says
/// "this one is doing something". All of that lives here so the window can be about messages and a test can ask
/// what the tools will do without one.
/// </para>
/// <para>
/// The palette is short on purpose: six colours and automatic, which is what a person wants to shout about in a
/// chat, and every one of them is dark enough to read on the white of the message box. They are offered as a menu
/// now rather than cycled through one click at a time - a cycle hides five sixths of what is on offer behind a
/// guess - and the menu is built from <see cref="Palette"/> itself, so what is on offer and what is listed cannot
/// come apart. <see langword="null"/> is in the list and means <i>automatic</i>: whatever colour this window already
/// writes messages in.
/// </para>
/// </summary>
/// <param name="Name">What the menu calls this choice, and what a person reads out to someone else.</param>
/// <param name="Color">The colour, or <see langword="null"/> for the window's own.</param>
public static class ComposerStyle
{
    /// <summary>The colour of the word in an empty box (see <see cref="MessageInput.Hint"/>). Grey against the
    /// box's white, so it reads as a prompt and not as something somebody typed.</summary>
    public static Color HintColor { get; } = Color.FromArgb(0x9A, 0x9A, 0x9A);

    /// <summary>The colour of a tool that is doing something.</summary>
    public static Color ToolOnColor { get; } = Color.FromArgb(0xD6, 0xE6, 0xF5);

    /// <summary>The colour of a tool that is not. The strip's own grey, so "off" is the absence of a mark.</summary>
    public static Color ToolOffColor { get; } = SystemColors.Control;

    /// <summary>One choice the colour tool offers: a name to read and a colour to wear.</summary>
    public sealed record Ink(string Name, Color? Color);

    /// <summary>
    /// The colours the colour tool offers, in the order its menu lists them, ending with automatic.
    /// <para>
    /// Every one of them is dark enough to read on the white of the message box, which rules out the yellows and
    /// the cyans - and every one of them is expressible as <c>#RRGGBB</c>, which is all the wire can say about
    /// colour anyway (T12.1). No opacity, no gradients, nothing that would arrive as something else.
    /// </para>
    /// <para>
    /// The names are part of the entry rather than a second list, because a palette and a list of names are two
    /// things that can be edited separately and then disagree, and the disagreement is a menu that says Green over a
    /// colour that arrives red.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Ink> Palette { get; } =
    [
        new Ink("Red", Color.FromArgb(0xC0, 0x00, 0x00)),
        new Ink("Orange", Color.FromArgb(0xC0, 0x60, 0x00)),
        new Ink("Green", Color.FromArgb(0x00, 0x80, 0x00)),
        new Ink("Blue", Color.FromArgb(0x00, 0x00, 0xC0)),
        new Ink("Purple", Color.FromArgb(0x70, 0x30, 0xA0)),
        new Ink("Pink", Color.FromArgb(0xC0, 0x00, 0x80)),
        new Ink("Automatic", null),
    ];

    /// <summary>
    /// The three sizes the size tool offers, smallest first: the order a menu reads in, which is not the order a
    /// cycle used to go round in.
    /// </summary>
    public static IReadOnlyList<int> Sizes { get; } = [StyledRun.Small, StyledRun.Normal, StyledRun.Large];

    /// <summary>What the size tool calls one of the three sizes on the wire.</summary>
    public static string SizeName(int size) => size switch
    {
        StyledRun.Small => "Small",
        StyledRun.Large => "Large",
        _ => "Normal",
    };

    /// <summary>
    /// The colour as the wire says it, or null for "nothing claimed". Null rather than an empty string because
    /// absence is what the protocol was built on: a run with no colour field is a run in the window's own colour.
    /// </summary>
    public static string? Hex(Color? colour)
        => colour is null ? null : $"#{colour.Value.R:X2}{colour.Value.G:X2}{colour.Value.B:X2}";

    /// <summary>
    /// The colour a run asks for, or null when it asks for none or asks for something this machine cannot make
    /// out. The same leniency as the transcript's (see <see cref="TranscriptStyle.RunColour"/>): a colour that
    /// cannot be parsed must not be allowed to take the sentence down with it.
    /// </summary>
    public static Color? Colour(string? hex) => TranscriptStyle.RunColour(new StyledRun(0, Color: hex));

    /// <summary>
    /// A font at one of the three sizes, against the box's own font.
    /// <para>
    /// The sizes themselves are decided by <see cref="TranscriptStyle"/>, the code that draws a received message,
    /// and not here: a word made large in the box has to come out the same size in the room, and two definitions of
    /// "large" is how a message arrives smaller than it was typed.
    /// </para>
    /// </summary>
    public static Font SizeFont(Font baseFont, int size)
    {
        ArgumentNullException.ThrowIfNull(baseFont);

        return TranscriptStyle.RunFont(baseFont, new StyledRun(0, Size: size)) ?? baseFont;
    }

    /// <summary>
    /// The side of a tool button that is a picture rather than a word - the face of the emoji tool and the page of
    /// the share tool - measured so that the whole of the picture is inside the whole of the button.
    /// <para>
    /// WinForms neither scales a button's <see cref="Button.Image"/> nor grows the button: it draws what it is given
    /// and the parts outside are cut, without a word of complaint. So the picture has to be chosen against the space
    /// it will be drawn in, which is the button's client area less a pixel of air at each edge - and that is also
    /// what makes a face look like a face rather than like a chin.
    /// </para>
    /// </summary>
    public static int PicturePixels(Control tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return Math.Max(12, Math.Min(tool.ClientSize.Width, tool.ClientSize.Height) - 4);
    }
}
