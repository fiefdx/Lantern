namespace Lantern.Domain;

/// <summary>
/// A picture in the chat (F7). The bytes travel in a frame body, never base64.
/// </summary>
public sealed class PictureMessage : Message
{
    /// <summary>Longest edge of the inline thumbnail, in pixels.</summary>
    public const int ThumbnailEdge = 160;

    public PictureMessage(string senderNickname, string roomName, string fileName, byte[] data, DateTime? sentAt = null, Guid? id = null)
        : base(senderNickname, roomName, MessageKind.PICTURE, sentAt, id)
    {
        FileName = fileName ?? string.Empty;
        Data = data ?? [];
    }

    public string FileName { get; }

    public byte[] Data { get; }

    public long SizeBytes => Data.LongLength;

    public override string Preview => $"[image] {FileName} ({SizeBytes:N0} bytes)";

    /// <summary>
    /// Scaled copy of the picture, longest edge capped at <see cref="ThumbnailEdge"/>.
    /// Returns null when the bytes are not a decodable image (the transcript then shows
    /// only <see cref="Preview"/>).
    /// </summary>
    public Image? ToThumbnail()
    {
        try
        {
            using var source = new MemoryStream(Data);
            using var image = Image.FromStream(source);

            var scale = Math.Min(1d, (double)ThumbnailEdge / Math.Max(image.Width, image.Height));
            var width = Math.Max(1, (int)Math.Round(image.Width * scale));
            var height = Math.Max(1, (int)Math.Round(image.Height * scale));

            var thumb = new Bitmap(width, height);
            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, 0, 0, width, height);
            }

            return thumb;
        }
        catch (OutOfMemoryException)
        {
            // Image.FromStream reports a bad codec/payload this way.
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
