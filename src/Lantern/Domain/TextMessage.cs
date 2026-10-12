namespace Lantern.Domain;

/// <summary>A plain chat line (F7).</summary>
public sealed class TextMessage : Message
{
    public TextMessage(string senderNickname, string roomName, string body, DateTime? sentAt = null, Guid? id = null)
        : base(senderNickname, roomName, MessageKind.TEXT, sentAt, id)
        => Body = body ?? string.Empty;

    public string Body { get; }

    /// <summary>
    /// What the line looks like, as runs laid end to end over <see cref="Body"/> (T12.1, v1.7). Empty is
    /// the normal case and means plain text.
    /// <para>
    /// An annotation and not a part of the message: <see cref="Body"/> is always present, always enough to
    /// read, and unchanged whether the runs are applied, thrown away by an older peer, or refused because
    /// they do not add up (see <see cref="StyledRun.Fits"/>). Nothing may put formatting <em>in</em> the
    /// body, because then the sentence would stop being the thing both machines agree on.
    /// </para>
    /// <para>
    /// <c>init</c> and not a constructor argument on purpose: every place that builds a message says
    /// <c>new TextMessage(sender, room, body)</c>, and a formatting round must not cost a parameter in two
    /// hundred call sites. Read it freely — <see cref="Room.Append"/> and the transcript render it — but
    /// nothing rewrites it: a message that changed its own formatting after being sent is a message that
    /// said something else.
    /// </para>
    /// </summary>
    public IReadOnlyList<StyledRun> Styles { get; init; } = [];

    /// <summary>True when the sender asked for anything beyond the window's own plain text.</summary>
    public bool IsStyled => Styles.Count > 0;

    /// <summary>
    /// The same line with these runs on it instead of the ones it had. Nothing else changes — not its id,
    /// not its moment, not one character of <see cref="Body"/> — because re-styled is the same message.
    /// </summary>
    /// <remarks>
    /// For whoever has to decide what a line's formatting actually was: a service that refuses part of a run
    /// list says so by handing back the line without it (<see cref="StyledRun.Accepted"/>), which keeps the
    /// decision with the thing that made it instead of leaving a window showing bold nobody else was sent.
    /// </remarks>
    public TextMessage WithStyles(IReadOnlyList<StyledRun> styles)
        => new(SenderNickname, RoomName, Body, SentAt, Id) { Styles = styles };

    public override string Preview => Body;
}
