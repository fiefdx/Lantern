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
}
