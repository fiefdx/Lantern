using System.Text.Json;
using Lantern.Core;
using Lantern.Domain;

namespace Lantern.Storage;

/// <summary>
/// The host's room list, kept in one JSON file (F9: rooms survive a restart; chat history never does).
/// </summary>
/// <remarks>
/// <para>
/// Two rules shape this class, and both come from "never crash" in F9.
/// </para>
/// <para>
/// <b>Loading is allowed to be disappointed but not surprised.</b> A missing file, an empty file, a file
/// containing <c>hello</c>, a file whose rooms have no names — all of them mean "start with the default
/// rooms" and none of them means an exception on the way to the first window of the session. The one
/// thing that is <em>not</em> replaced with defaults is a file that genuinely lists no rooms: a host that
/// deleted every room should not get three back.
/// </para>
/// <para>
/// <b>Saving is a rename.</b> The new list is written to <c>rooms.json.tmp</c> and then moved onto
/// <c>rooms.json</c>, so a power cut halfway through writing leaves the previous file intact instead of
/// a half-written one that the next start has to throw away.
/// </para>
/// <para>
/// A failed save is reported and swallowed. Losing the save costs the room list at the <em>next</em>
/// start; the running chat is unaffected, and interrupting a conversation because a file is open in
/// Notepad would be a much worse failure. The host's window is expected to show
/// <see cref="SaveFailed"/> on its status line.
/// </para>
/// </remarks>
public sealed class JsonRoomStore : IRoomStore, IDisposable
{
    /// <summary>The file name used under <see cref="DefaultDirectory"/>.</summary>
    public const string FileName = "rooms.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly string path;
    private bool disposed;

    /// <summary>Stores the list in <c>%LOCALAPPDATA%\Lantern\rooms.json</c>.</summary>
    public JsonRoomStore()
        : this(Path.Combine(DefaultDirectory, FileName))
    {
    }

    /// <summary>Stores the list at an explicit path — what the tests use, and anything else that wants a say.</summary>
    public JsonRoomStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    /// <inheritdoc />
    public event EventHandler<string>? SaveFailed;

    /// <summary>Where the default instance keeps the file: per-user application data, which is writable.</summary>
    public static string DefaultDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lantern");

    /// <inheritdoc />
    public List<Room> Load()
    {
        string text;

        try
        {
            if (!File.Exists(path))
            {
                return Defaults();      // first run
            }

            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Unreadable is the same as absent as far as the user is concerned: they get a chat.
            return Defaults();
        }

        var entries = ReadEntries(text);

        if (entries is null)
        {
            return Defaults();      // not a room list at all
        }

        var rooms = new List<Room>();

        // An entry is a name or a hole where a name should have been. Holes are dropped rather than
        // poisoning the whole file: the other rooms are somebody's work.
        foreach (var entry in entries.Where(e => e is not null))
        {
            var name = ValidationRules.Normalize(entry);

            if (!ValidationRules.IsValidRoomName(name))
            {
                continue;
            }

            // Room names are matched case-insensitively everywhere else, so two that differ only in case
            // would be one room with two spellings. The first spelling wins.
            if (rooms.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            rooms.Add(new Room(name));
        }

        // Nothing usable, but the file did list something: it is junk, and junk gets the defaults.
        // An empty list in the file is different - that is a host that deleted everything, and it stays empty.
        return rooms.Count > 0 || entries.Count == 0 ? rooms : Defaults();
    }

    /// <inheritdoc />
    public async Task SaveAsync(IEnumerable<Room> rooms, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(rooms);

        if (disposed)
        {
            throw new ObjectDisposedException(nameof(JsonRoomStore));
        }

        // Snapshot first: the caller's room list belongs to the server and may change while this awaits.
        var file = new RoomsFileDto
        {
            Version = LanternProtocol.Version,
            Rooms = [.. rooms.Select(r => new RoomDto { Name = r.Name })],
        };

        var text = JsonSerializer.Serialize(file, Json);
        var temp = path + ".tmp";

        await writeLock.WaitAsync(cancel).ConfigureAwait(false);

        try
        {
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(temp, text, cancel).ConfigureAwait(false);

            // One operation replaces the file, so there is no moment where the room list is nothing.
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                   or NotSupportedException or ArgumentException or ObjectDisposedException)
        {
            // The list in memory is still right, and the chat is still running. Say so, quietly.
            TryDelete(temp);
            Dispatch.Safe(
                () => SaveFailed?.Invoke(this, $"Could not save the room list to {path}: {ex.Message}"));
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>The path this store reads and writes, for the status line and for tests.</summary>
    public override string ToString() => path;

    /// <summary>Releases the save lock. Nothing else is held open between calls.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        writeLock.Dispose();
    }

    private static List<Room> Defaults() => [.. LanternProtocol.DefaultRoomNames.Select(name => new Room(name))];

    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (IOException)
        {
            // A leftover .tmp is untidy and harmless; the next save overwrites it.
        }
    }

    /// <summary>
    /// The entries of a room list, each one a name or null where a name should have been; null result
    /// means the text is not a room list at all.
    /// </summary>
    /// <remarks>
    /// Two shapes are accepted: what this class writes, and the bare array that somebody editing the
    /// file by hand would write. A file that a marker might produce is a file the program has to answer,
    /// because F9 is checked by opening it in Notepad.
    /// </remarks>
    private static List<string?>? ReadEntries(string text)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("rooms", out var rooms)
                && rooms.ValueKind == JsonValueKind.Array)
            {
                return NamesOf(rooms.EnumerateArray());
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                return NamesOf(root.EnumerateArray());
            }

            return null;
        }
    }

    private static List<string?> NamesOf(IEnumerable<JsonElement> entries)
    {
        var names = new List<string?>();

        foreach (var entry in entries)
        {
            string? name = entry.ValueKind switch
            {
                JsonValueKind.String => entry.GetString(),
                JsonValueKind.Object
                    when entry.TryGetProperty("name", out var property) && property.ValueKind == JsonValueKind.String
                    => property.GetString(),
                _ => null,
            };

            // A hole is kept as a hole: a file that lists three things and gives no name for any of them
            // is junk, while a file that lists nothing at all is a decision. Load() tells those apart.
            names.Add(name);
        }

        return names;
    }
}
