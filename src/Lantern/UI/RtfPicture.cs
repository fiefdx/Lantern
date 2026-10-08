using System.Text;
using Lantern.Domain;

namespace Lantern.UI;

/// <summary>
/// The one place that knows how to put a picture inside a <see cref="RichTextBox"/> (F7).
/// <para>
/// A RichTextBox has no "insert image" call. It shows a picture only when it is handed RTF containing
/// a <c>\pict</c> group, so the thumbnail is re-encoded as PNG and written as hex inside that group.
/// Re-encoding whatever format arrived (JPEG, GIF, BMP) as PNG means one code path and one picture
/// type on the clipboard of the transcript, at the cost of a few kilobytes - which a 160 pixel
/// thumbnail is not short of anyway.
/// </para>
/// </summary>
public static class RtfPicture
{
    /// <summary>
    /// Twips per screen pixel. RTF measures a picture in twips (1/1440 of an inch) and a bitmap is
    /// 96 to the inch, so 1440 / 96.
    /// </summary>
    public const int TwipsPerPixel = 15;

    /// <summary>
    /// RTF for one thumbnail, or null when the image cannot be encoded. Returning null rather than
    /// throwing is what lets the transcript fall back to the text line: a picture the sender's camera
    /// produced in a format GDI+ cannot read is still a message that arrived, and a window that threw
    /// here would lose the line and everything after it.
    /// </summary>
    public static string? Inline(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        byte[] png;
        try
        {
            using var buffer = new MemoryStream();
            image.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
            png = buffer.ToArray();
        }
        catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }

        if (png.Length == 0)
        {
            return null;
        }

        var width = Math.Max(1, image.Width);
        var height = Math.Max(1, image.Height);

        var rtf = new StringBuilder(png.Length * 2 + 256);
        rtf.Append(@"{\rtf1\ansi\ansicpg1252\deff0\deflang1033");
        rtf.Append(@"\pard\plain\fs20 ");
        rtf.Append(@"{\pict\pngblip");
        rtf.Append(@"\picw").Append(width);
        rtf.Append(@"\pich").Append(height);
        rtf.Append(@"\picwgoal").Append(width * TwipsPerPixel);
        rtf.Append(@"\pichgoal").Append(height * TwipsPerPixel);
        rtf.Append(' ');

        foreach (var b in png)
        {
            rtf.Append(b.ToString("x2"));
        }

        rtf.Append('}');
        rtf.Append('}');

        return rtf.ToString();
    }

    /// <summary>
    /// RTF for a picture message's thumbnail, or null when there is no decodable image to show. The
    /// thumbnail is disposed here: the caller gets text, not a bitmap to look after.
    /// </summary>
    public static string? InlineFor(PictureMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var thumbnail = message.ToThumbnail();

        if (thumbnail is null)
        {
            return null;
        }

        try
        {
            return Inline(thumbnail);
        }
        finally
        {
            thumbnail.Dispose();
        }
    }
}
