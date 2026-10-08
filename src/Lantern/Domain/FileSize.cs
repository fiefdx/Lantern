namespace Lantern.Domain;

/// <summary>
/// Byte counts in the form a person reads (F7, F8). One place, because the share notice, the
/// incoming-file question and the transcript line all quote the same size and must not disagree -
/// "1.5 MB" in one and "1572864 bytes" in another reads like two different files.
/// </summary>
public static class FileSize
{
    /// <summary>Rounded to one decimal, switching unit at 1 KB and 1 MB.</summary>
    public static string Describe(long bytes) => bytes switch
    {
        < 0 => "0 B",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):0.##} GB",
    };
}
