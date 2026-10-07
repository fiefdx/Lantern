using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lantern.Transport;

/// <summary>
/// The JSON half of a frame: everything about a message except its bulk bytes, which live in
/// <see cref="Frame.Body"/>. Every field is optional because one record covers all twelve frame
/// types — <see cref="Frame"/> factories decide which ones get set, and nothing is ever written
/// when it is null, so a chat line stays tiny.
/// </summary>
public sealed record FrameHeader
{
    /// <summary>Options shared by the codec and the tests.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Which factory methods apply and which fields must be present.</summary>
    [JsonPropertyName("t")]
    public FrameType Type { get; init; }

    /// <summary>
    /// Message id, so a re-delivered frame can be told from a new one. Also the join key between a
    /// FILE_META and the transcript line it announced.
    /// </summary>
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    /// <summary>Room the frame is about. Absent for frames that precede any room (HELLO).</summary>
    [JsonPropertyName("room")]
    public string? Room { get; init; }

    /// <summary>Who sent it, or whose nickname is being offered in HELLO.</summary>
    [JsonPropertyName("nick")]
    public string? Nickname { get; init; }

    /// <summary>
    /// What the host prefers to be called, echoed in HELLO_ACK. Discovery hands a client an address;
    /// this is what turns <c>127.0.0.1:43797</c> into a name in the status bar.
    /// </summary>
    [JsonPropertyName("server")]
    public string? ServerName { get; init; }

    /// <summary>Text of a TEXT frame, or of a SYSTEM_NOTICE.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>When the message left its author — the transcript shows this, not arrival time.</summary>
    [JsonPropertyName("at")]
    public DateTime? SentAt { get; init; }

    /// <summary>Room names for ROOM_LIST.</summary>
    [JsonPropertyName("rooms")]
    public IReadOnlyList<string>? Rooms { get; init; }

    /// <summary>Members for MEMBER_LIST.</summary>
    [JsonPropertyName("members")]
    public IReadOnlyList<FrameMember>? Members { get; init; }

    /// <summary>File name for FILE_META — a name, never a path.</summary>
    [JsonPropertyName("file")]
    public string? FileName { get; init; }

    /// <summary>Total byte count for FILE_META, so the receiver can size a progress bar.</summary>
    [JsonPropertyName("size")]
    public long? SizeBytes { get; init; }

    /// <summary>Ties FILE_META, FILE_CHUNK and FILE_DONE to one another.</summary>
    [JsonPropertyName("transfer")]
    public Guid? TransferId { get; init; }

    /// <summary>Chunk number, starting at zero; lets the receiver notice a gap.</summary>
    [JsonPropertyName("seq")]
    public int? Sequence { get; init; }

    /// <summary>Reason for an ERROR frame.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static FrameHeader FromJson(string json)
        => JsonSerializer.Deserialize<FrameHeader>(json, JsonOptions)
           ?? throw new FrameException("Frame header was empty.");
}

/// <summary>One entry of a MEMBER_LIST frame.</summary>
public sealed record FrameMember(
    [property: JsonPropertyName("id")] string ConnectionId,
    [property: JsonPropertyName("nick")] string Nickname);
