namespace Lantern.Storage;

/// <summary>
/// The copy of something a peer sent that this machine keeps so it can be opened (T5.2).
/// <para>
/// A picture arrives as bytes in a frame; showing the 160 pixel thumbnail in the transcript is fine,
/// but a person who wants to see the photo wants the photo. It has to be a file somewhere for the
/// system's own viewer to open it, and where that is - and what it is called when the sender called it
/// <c>../../windows/system32/config/sam</c> - is this class's whole job.
/// </para>
/// </summary>
public static class ReceivedPayloads
{
    /// <summary>
    /// Where the bytes of a file that is arriving are kept while the person decides where to put it (F8).
    /// Under the user's own area rather than beside the program, because a file arriving from another
    /// machine is not something to write into a shared folder or wherever the shortcut pointed.
    /// </summary>
    public static string IncomingDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lantern",
        "incoming");

    /// <summary>Longest file name kept from a peer, after the id prefix and before the extension.</summary>
    public const int NameBudget = 60;

    /// <summary>Where copies go when nothing else is asked for: the per-user app data folder.</summary>
    public static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lantern",
        "cache");

    /// <summary>
    /// A name for a peer's file that is safe to create on this machine: the sender's id comes first so
    /// two photos both called <c>photo.jpg</c> are two files, and the sender's name is reduced to a
    /// last path component with the characters Windows refuses removed.
    /// </summary>
    public static string FileNameFor(string? suggested, Guid id)
    {
        var name = Path.GetFileName((suggested ?? string.Empty).Trim());

        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(bad, '_');
        }

        // A path of dots is what ".." and "." are, and neither is a file to create.
        name = name.TrimStart('.');

        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);

        if (stem.Length > NameBudget)
        {
            stem = stem[..NameBudget];
        }

        if (stem.Length == 0)
        {
            stem = "received";
        }

        return $"{id:N}-{stem}{extension}";
    }

    /// <summary>
    /// Writes the bytes and returns the path. Saving the same message twice is a read, not a second
    /// write: the file is complete or the message has not arrived, and re-saving what is already there
    /// would only truncate a file somebody has open in a viewer.
    /// </summary>
    public static string Save(string? suggested, Guid id, byte[] data, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        var folder = string.IsNullOrWhiteSpace(directory) ? CacheDirectory : directory;
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, FileNameFor(suggested, id));

        if (File.Exists(path) && new FileInfo(path).Length == data.LongLength)
        {
            return path;
        }

        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>
    /// Opens a file with whatever the system has for it. False when that is not possible - no viewer
    /// for the extension is the usual case, and it is a shrug, not an error: the file is still on disk
    /// and the status bar says where.
    /// </summary>
    public static bool TryOpen(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true,
            });

            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }
}
