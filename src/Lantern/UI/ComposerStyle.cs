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
/// The palette is a short cycle rather than a picker. Eight colours cover what a person wants to shout about in a
/// chat, a cycle is one control and no popup, and every choice is a click on a named button instead of a click
/// inside a window that a test cannot see into. <see langword="null"/> is in the list and means <i>automatic</i>:
/// whatever colour this window already writes messages in.
/// </para>
/// </summary>
public static class ComposerStyle
{
    /// <summary>The colour of the word in an empty box (see <see cref="MessageInput.Hint"/>). Grey against the
    /// box's white, so it reads as a prompt and not as something somebody typed.</summary>
    public static Color HintColor { get; } = Color.FromArgb(0x9A, 0x9A, 0x9A);

    /// <summary>The colour of a tool that is doing something.</summary>
    public static Color ToolOnColor { get; } = Color.FromArgb(0xD6, 0xE6, 0xF5);

    /// <summary>The colour of a tool that is not. The strip's own grey, so "off" is the absence of a mark.</summary>
    public static Color ToolOffColor { get; } = SystemColors.Control;

    /// <summary>
    /// The colours the colour tool offers, in the order it cycles them, ending with automatic.
    /// <para>
    /// Every one of them is dark enough to read on the white of the message box, which rules out the yellows and
    /// the cyans - and every one of them is expressible as <c>#RRGGBB</c>, which is all the wire can say about
    /// colour anyway (T12.1). No opacity, no gradients, nothing that would arrive as something else.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Color?> Palette { get; } =
    [
        Color.FromArgb(0xC0, 0x00, 0x00),
        Color.FromArgb(0xC0, 0x60, 0x00),
        Color.FromArgb(0x00, 0x80, 0x00),
        Color.FromArgb(0x00, 0x00, 0xC0),
        Color.FromArgb(0x70, 0x30, 0xA0),
        Color.FromArgb(0xC0, 0x00, 0x80),
        null,
    ];

    /// <summary>
    /// The sizes the size tool offers, in the order it cycles them from wherever it is. Normal comes straight
    /// after large so that two clicks from the default get to small, rather than going the long way round.
    /// </summary>
    public static IReadOnlyList<int> Sizes { get; } = [StyledRun.Large, StyledRun.Small, StyledRun.Normal];

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

    /// <summary>The next thing the colour tool offers, given the one it is showing.</summary>
    public static Color? NextColour(Color? current)
    {
        for (var at = 0; at < Palette.Count; at++)
        {
            if (Palette[at] == current)
            {
                return Palette[(at + 1) % Palette.Count];
            }
        }

        return Palette[0];
    }

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

    /// <summary>The next size the size tool offers, given the one it is showing.</summary>
    public static int NextSize(int current)
    {
        for (var at = 0; at < Sizes.Count; at++)
        {
            if (Sizes[at] == current)
            {
                return Sizes[(at + 1) % Sizes.Count];
            }
        }

        return Sizes[0];
    }
}
