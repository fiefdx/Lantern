namespace Lantern.Domain;

/// <summary>
/// Central place for the input rules of the app (T1.4). Every UI entry point
/// validates through these members so the message shown to the user is identical
/// everywhere and unit-testable without a form.
/// </summary>
public static class ValidationRules
{
    public const int NicknameMinLength = 1;
    public const int NicknameMaxLength = 16;

    public const int RoomNameMinLength = 1;
    public const int RoomNameMaxLength = 24;

    public const int MessageMaxLength = 500;

    /// <summary>Pictures travel in one frame body, so keep them modest.</summary>
    public const long MaxPictureBytes = 5L * 1024 * 1024;

    /// <summary>
    /// How long some text is, in the characters a person counts rather than the ones UTF-16 stores.
    /// </summary>
    /// <remarks>
    /// An emoji is two UTF-16 units - a surrogate pair - so measuring with <c>string.Length</c> told somebody that
    /// their 250 faces were a 500 character message, and the refusal said "500 characters or fewer" to a person
    /// counting half as many as they had typed. This counts text elements, which also means a family joined with
    /// zero-width joiners and a flag made of two regional indicators count once each, as they look.
    /// </remarks>
    public static int Length(string? text)
        => string.IsNullOrEmpty(text) ? 0 : new System.Globalization.StringInfo(text).LengthInTextElements;

    /// <summary>Trim + collapse inner whitespace; never returns null.</summary>
    public static string Normalize(string? raw)
        => string.Join(' ', (raw ?? string.Empty).Split(default(string[]?),
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>null when the nickname is acceptable, otherwise the reason to show.</summary>
    public static string? NicknameError(string? raw)
    {
        var nickname = Normalize(raw);
        if (nickname.Length == 0)
        {
            return "Nickname is required.";
        }

        if (nickname.Length < NicknameMinLength)
        {
            return $"Nickname must be at least {NicknameMinLength} character(s).";
        }

        if (Length(nickname) > NicknameMaxLength)
        {
            return $"Nickname must be {NicknameMaxLength} characters or fewer.";
        }

        return null;
    }

    public static bool IsValidNickname(string? raw) => NicknameError(raw) is null;

    /// <summary>null when the room name is acceptable, otherwise the reason to show.</summary>
    public static string? RoomNameError(string? raw)
    {
        var name = Normalize(raw);
        if (name.Length == 0)
        {
            return "Room name is required.";
        }

        if (Length(name) > RoomNameMaxLength)
        {
            return $"Room name must be {RoomNameMaxLength} characters or fewer.";
        }

        return null;
    }

    public static bool IsValidRoomName(string? raw) => RoomNameError(raw) is null;

    /// <summary>Case-insensitive duplicate check used by the host's room CRUD.</summary>
    public static bool IsDuplicateRoomName(string name, IEnumerable<string> existing)
        => existing.Any(e => string.Equals(Normalize(e), Normalize(name), StringComparison.OrdinalIgnoreCase));

    /// <summary>null when the message body is acceptable, otherwise the reason to show.</summary>
    public static string? MessageError(string? raw)
    {
        var body = raw ?? string.Empty;
        if (body.Trim().Length == 0)
        {
            return "Message is empty.";
        }

        if (Length(body) > MessageMaxLength)
        {
            return $"Message must be {MessageMaxLength} characters or fewer.";
        }

        return null;
    }

    public static bool IsValidMessage(string? raw) => MessageError(raw) is null;

    /// <summary>null when a picture is acceptable, otherwise the reason to show.</summary>
    public static string? PictureError(string? fileName, long byteCount)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Picture has no file name.";
        }

        if (byteCount <= 0)
        {
            return "Picture file is empty.";
        }

        if (byteCount > MaxPictureBytes)
        {
            return $"Picture is larger than {MaxPictureBytes / (1024 * 1024)} MB.";
        }

        return null;
    }

    public static bool IsValidPicture(string? fileName, long byteCount)
        => PictureError(fileName, byteCount) is null;
}
