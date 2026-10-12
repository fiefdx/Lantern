using Lantern.Domain;

namespace Lantern.UI;

/// <summary>
/// How one message is written in the transcript (F13): a <b>headline</b> that says when it was sent and who
/// sent it, then the message itself on the line below, in a different colour.
/// <para>
/// Both windows draw the transcript, the transcript is rebuilt from a room's history whenever somebody
/// switches rooms, and the file-transfer lines and notices are drawn by the same code as the lines people
/// type. So the shape of an entry lives in one place where all of them can be drawn from it and where a test
/// can ask what an entry looks like without a window, a caret or a colour picker - which is what this class
/// is for. <see cref="Lantern.UI.ChatForm"/> does the painting; this decides it.
/// </para>
/// <para>
/// The headline is on its own line because a group chat is read as a log: with the sender, the time and the
/// text on one line, a room full of people turns into a wall where nothing lines up and the eye cannot find
/// who said what. Two colours for the two parts so the scaffolding is visibly not the message - the parts
/// that are always there (when, who) recede, and what a person actually typed is what stands out.
/// </para>
/// </summary>
public static class TranscriptStyle
{
    /// <summary>
    /// The date as well as the clock time. A group chat stays open across an afternoon, and a transcript that
    /// says only "21:04" cannot tell you whether that was today - which matters most exactly when somebody
    /// asks "when did that arrive?", and matters to the person reading a saved log tomorrow.
    /// </summary>
    public const string SentAtFormat = "yyyy-MM-dd HH:mm";

    /// <summary>Who and when, in the colour that recedes. A notice has no sender, so it gets the time alone.</summary>
    public static string Headline(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sentAt = $"[{message.SentAt.ToString(SentAtFormat, System.Globalization.CultureInfo.CurrentCulture)}]";

        return message.SenderNickname.Length == 0 ? sentAt : $"{sentAt} {message.SenderNickname}";
    }

    /// <summary>
    /// What the message says, on the line below its headline. Multi-line text stays multi-line: it is the
    /// person's own line breaks, and a transcript is not the place to flatten them.
    /// </summary>
    public static string Content(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.Preview;
    }

    // -------------------------------------------------------------- colours
    //
    // Every colour here is chosen against the surface below, and the surface is decided here too. That is not
    // fastidiousness: a read-only RichTextBox paints itself with SystemColors.Control - the window's grey - so
    // "leave it to the system" produced a grey chat area in a window whose room list and message box were white,
    // and a colour that follows the theme is only safe next to a background that follows the same theme. Pin one
    // and you have to pin the other. TranscriptLayoutTests measures the pair rather than trusting the intent.

    /// <summary>
    /// The surface every entry is written on: white, like the room list, the member list and the box the message
    /// is typed in. A read-only text area does <b>not</b> default to this - it defaults to the window's grey.
    /// </summary>
    public static Color BackgroundColor { get; } = Color.White;

    /// <summary>Who and when: dark blue. Deliberately not the colour of the message.</summary>
    public static Color HeadlineColor { get; } = Color.FromArgb(0x1F, 0x4E, 0x79);

    /// <summary>What was said: black, because the transcript is a fixed white (see <see cref="BackgroundColor"/>).</summary>
    public static Color ContentColor { get; } = Color.Black;

    /// <summary>
    /// A join or leave notice is not a person talking, so it is quiet: mid-grey where a person's headline
    /// would be blue. Still a different colour from its own content, because the shape of an entry does not
    /// change from message to message - one rule, read at a glance.
    /// </summary>
    public static Color NoticeHeadlineColor { get; } = Color.FromArgb(0x6E, 0x6E, 0x6E);

    /// <summary>The notice's own words, in a grey dark enough to read easily and light enough to ignore.</summary>
    public static Color NoticeContentColor { get; } = Color.FromArgb(0x40, 0x40, 0x40);

    /// <summary>The colour this message's headline is painted in.</summary>
    public static Color HeadlineColorFor(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.Kind == MessageKind.JOIN_LEAVE_NOTICE ? NoticeHeadlineColor : HeadlineColor;
    }

    /// <summary>The colour this message's content is painted in.</summary>
    public static Color ContentColorFor(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.Kind == MessageKind.JOIN_LEAVE_NOTICE ? NoticeContentColor : ContentColor;
    }

    // -------------------------------------------------------------- runs
    //
    // A run off the wire says bold, italic, underline, one of three sizes, maybe a colour. That is a description of
    // ink rather than ink: somebody still has to say what "large" is in points, next to this font, on this machine.
    // Decided here, so that the window drawing it and the test measuring it agree on one answer, and so that the
    // answer can change without anything on the link changing with it - the wire says "large", and only this file
    // knows what large looks like.

    /// <summary>How much smaller than the room's own font a small run is, in points.</summary>
    public const float SmallDelta = -2f;

    /// <summary>
    /// How much larger than the room's own font a large run is. Not the mirror of <see cref="SmallDelta"/>: text
    /// that is merely small reads as a footnote, and emphasis that is merely big reads as emphasis.
    /// </summary>
    public const float LargeDelta = 4f;

    /// <summary>
    /// The font one run is written in, or null when the run asks for nothing and the control's own font should be
    /// used. Null rather than a copy of the base font because the caller then knows not to allocate or dispose
    /// anything for the overwhelmingly common case of an unstyled line.
    /// </summary>
    public static Font? RunFont(Font baseFont, StyledRun run)
    {
        ArgumentNullException.ThrowIfNull(baseFont);

        var style = FontStyle.Regular;

        if (run.Bold)
        {
            style |= FontStyle.Bold;
        }

        if (run.Italic)
        {
            style |= FontStyle.Italic;
        }

        if (run.Underline)
        {
            style |= FontStyle.Underline;
        }

        var size = run.Size switch
        {
            StyledRun.Small => baseFont.Size + SmallDelta,
            StyledRun.Large => baseFont.Size + LargeDelta,
            _ => baseFont.Size,
        };

        // Never smaller than the smallest thing worth drawing: a peer from another machine could claim a size this
        // font has no room for, and invisible text is worse than text the wrong size.
        if (size < 4f)
        {
            size = baseFont.Size;
        }

        if (style == FontStyle.Regular && size == baseFont.Size)
        {
            return null;
        }

        if (size == baseFont.Size)
        {
            // The common case, and the only reason a run that is merely bold does not go looking for a font family:
            // Font(Font, FontStyle) copies the family and size it is given.
            return new Font(baseFont, style);
        }

        // Rescaling has to name a family, which is the one place this could fail on a machine without the
        // transcript's font - so the size is applied first and the style copied off the result, and the intermediate
        // font is handed back before anything is drawn with it.
        using var rescaled = new Font(baseFont.Name, size, baseFont.Style, baseFont.Unit, baseFont.GdiCharSet, baseFont.GdiVerticalFont);

        return new Font(rescaled, style);
    }

    /// <summary>
    /// The colour a run asked for, or null when it asked for none. Anything unparseable reads as "none" rather than
    /// throwing: a colour this machine cannot make must not be allowed to take the sentence down with it.
    /// </summary>
    public static Color? RunColour(StyledRun run)
    {
        if (run.Color is not { Length: 7 } colour || colour[0] != '#')
        {
            return null;
        }

        var rgb = new int[3];

        for (var part = 0; part < 3; part++)
        {
            var high = Digit(colour[1 + (part * 2)]);
            var low = Digit(colour[2 + (part * 2)]);

            if (high < 0 || low < 0)
            {
                return null;
            }

            rgb[part] = (high * 16) + low;
        }

        return Color.FromArgb(rgb[0], rgb[1], rgb[2]);
    }

    private static int Digit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };
}
