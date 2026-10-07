namespace Lantern.Storage;

/// <summary>
/// One room as it appears in <c>rooms.json</c>. A room name and nothing else: members and transcripts
/// live exactly as long as the process does, which is requirement F9 rather than a limitation of it.
/// </summary>
/// <remarks>
/// A separate type from <see cref="Lantern.Domain.Room"/> on purpose. A <c>Room</c> holds a live
/// collection of who is inside and a transcript, so serialising it directly would either write history
/// to disk — against F9 — or need a battery of attributes to stop it doing so. This type cannot write
/// the wrong thing.
/// </remarks>
internal sealed record RoomDto
{
    /// <summary>The room's name, as the host typed it.</summary>
    public required string Name { get; init; }
}

/// <summary>
/// The whole file. The wrapper exists so that a future format can be recognised instead of
/// misread, and so the file says what it is when someone opens it in Notepad.
/// </summary>
internal sealed record RoomsFileDto
{
    /// <summary>Format version, written for whoever comes next. Reading does not depend on it.</summary>
    public int Version { get; init; }

    /// <summary>Every room, in the order the host's window lists them.</summary>
    public List<RoomDto> Rooms { get; init; } = [];
}
