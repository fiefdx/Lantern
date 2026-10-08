namespace Lantern.Domain;

/// <summary>
/// The one Share button's fork in the road (F7 against F8): is this thing a picture to look at, or a
/// file to receive?
/// <para>
/// The person clicking has a thing they want to send and should not have to answer a second question about
/// what kind of thing it is. A file's own name is the honest clue - it is what every viewer on the machine
/// uses too - so the extension decides, and anything unrecognised becomes a file, which is the safe
/// answer: a file arrives complete and the person opens it themselves, whereas a picture that is not a
/// picture is a message with a hole where the image should be.
/// </para>
/// </summary>
public static class Attachments
{
    /// <summary>
    /// The few formats a photo ends up in. Held in one list so the picker's filter, the decision here and
    /// the words in the status bar cannot drift apart.
    /// </summary>
    public static readonly IReadOnlyList<string> PictureExtensions =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp",
    ];

    /// <summary>The picker's filter string, built from <see cref="PictureExtensions"/>.</summary>
    public static string PictureFilter
        => $"Pictures ({string.Join(';', PictureExtensions.Select(e => "*" + e))})|{string.Join(';', PictureExtensions.Select(e => "*" + e))}";

    /// <summary>True when this name should travel as a picture (F7) rather than as a file (F8).</summary>
    public static bool IsPicture(string? path)
    {
        var extension = Path.GetExtension(path ?? string.Empty);

        return extension.Length > 0
            && PictureExtensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
    }
}
