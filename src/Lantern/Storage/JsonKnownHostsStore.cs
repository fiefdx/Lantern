using System.Text.Json;
using Lantern.Core;
using Lantern.Transport;

namespace Lantern.Storage;

/// <summary>
/// The machines this one has joined before (F2's other half).
/// </summary>
/// <remarks>
/// A Bluetooth scan on Windows lists what is paired and what answers an inquiry — and a laptop running
/// a Lantern host is usually neither (T4.1 measured one webOS TV found in twelve seconds, and a roomful
/// of paired-but-not-discoverable machines). So the machines worth offering are largely the ones this
/// machine has talked to before, which means somebody has to remember them.
/// </remarks>
public interface IKnownHosts
{
    /// <summary>Most recently joined first.</summary>
    IReadOnlyList<ServerEndpoint> Recent();

    /// <summary>
    /// Note that a join happened. Adding the same host again moves it to the front rather than listing
    /// it twice.
    /// </summary>
    void Remember(ServerEndpoint endpoint);

    /// <summary>A write that could not happen, in the store's own words. Reported, never thrown.</summary>
    event EventHandler<string>? SaveFailed;

    /// <summary>Every save asked for so far, for shutdown and for tests.</summary>
    Task SavesCompletedAsync();
}

/// <summary>
/// The known hosts in <c>%LOCALAPPDATA%\Lantern\known-hosts.json</c>, next to the rooms file and with
/// the same manners: a missing or unreadable file means "no history", never an exception, and a write is
/// a temporary file renamed so a power cut cannot leave half a list (T3.1).
/// </summary>
public sealed class JsonKnownHostsStore : IKnownHosts, IDisposable
{
    /// <summary>How much history to keep. Ten is a term's worth of project partners.</summary>
    public const int Limit = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string path;
    private readonly object gate = new();
    private readonly List<HostDto> hosts = [];
    private Task saves = Task.CompletedTask;
    private bool loaded;

    /// <summary>The default file for the current user.</summary>
    public JsonKnownHostsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lantern",
            "known-hosts.json"))
    {
    }

    /// <summary>A file of your choosing, which is what the tests use.</summary>
    public JsonKnownHostsStore(string path) => this.path = path;

    /// <inheritdoc />
    public event EventHandler<string>? SaveFailed;

    /// <inheritdoc />
    public IReadOnlyList<ServerEndpoint> Recent()
    {
        lock (gate)
        {
            EnsureLoaded();
            return hosts.Select(ToEndpoint).ToArray();
        }
    }

    /// <inheritdoc />
    public void Remember(ServerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var address = Normalise(endpoint.DeviceAddress);

        if (address.Length == 0)
        {
            return;
        }

        var name = endpoint.ServerName.Trim();
        var entry = new HostDto
        {
            // A host with no name is listed by its address, so there is nothing to store.
            Name = name == address ? string.Empty : name,
            Address = address,
        };

        List<HostDto> snapshot;

        lock (gate)
        {
            EnsureLoaded();

            hosts.RemoveAll(known => string.Equals(known.Address, address, StringComparison.OrdinalIgnoreCase));
            hosts.Insert(0, entry);
            if (hosts.Count > Limit)
            {
                hosts.RemoveRange(Limit, hosts.Count - Limit);
            }

            snapshot = hosts.ToList();
        }

        QueueSave(snapshot);
    }

    /// <inheritdoc />
    public Task SavesCompletedAsync()
    {
        lock (gate)
        {
            return saves;
        }
    }

    /// <summary>Where the list lives, which is how a test tells "empty" from "never used".</summary>
    public string FilePath => path;

    private void EnsureLoaded()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        hosts.Clear();
        hosts.AddRange(Read(path));
    }

    private void QueueSave(List<HostDto> snapshot)
    {
        Task previous;

        lock (gate)
        {
            previous = saves;

            saves = Task.Run(async () =>
            {
                try
                {
                    await previous.ConfigureAwait(false);
                }
                catch
                {
                    // Whoever failed reported it already; this list still has to be written.
                }

                try
                {
                    Write(path, snapshot);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Losing the history is not worth troubling anybody with; the chat is fine.
                    SaveFailed?.Invoke(this, $"Could not save the list of known hosts: {ex.Message}");
                }
            });
        }
    }

    private static IReadOnlyList<HostDto> Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var text = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            var onDisk = JsonSerializer.Deserialize<HostFile>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (onDisk?.Hosts is null)
            {
                return [];
            }

            return onDisk.Hosts
                .Where(host => !string.IsNullOrWhiteSpace(host.Address))
                .Select(host => new HostDto
                {
                    Name = host.Name?.Trim() ?? string.Empty,
                    Address = host.Address.Trim(),
                })
                .Where(host => IsDialable(host.Address))
                .Take(Limit)
                .ToArray();
        }
        catch (Exception)
        {
            // A file nobody can read is no history, and nothing else. F9's rule, applied here.
            return [];
        }
    }

    /// <summary>
    /// Twelve upper-case digits for anything that is a Bluetooth address, and the text as it came for
    /// everything else. Windows writes the same machine as AC86A3294B28, ac-86-a3-29-4b-28 and
    /// ac:86:a3:29:4b:28 depending on which screen it came from, and three spellings of one laptop would
    /// be three lines in the join list of which two fail.
    /// </summary>
    private static string Normalise(string address)
    {
        var trimmed = address.Trim();

        return RfcommAddress.TryParse(trimmed, out var parsed) ? RfcommAddress.Format(parsed) : trimmed;
    }

    /// <summary>
    /// Whether this entry could ever be dialled: a Bluetooth address, or a host with a port. An entry that
    /// cannot be dialled would be a line in the join list that only fails.
    /// </summary>
    private static bool IsDialable(string address)
    {
        if (address.Length > 64)
        {
            return false;
        }

        if (RfcommAddress.TryParse(address, out _))
        {
            return true;
        }

        var separator = address.LastIndexOf(':');

        return separator > 0
            && separator < address.Length - 1
            && int.TryParse(address[(separator + 1)..], out var port)
            && port is >= 1 and <= 65535;
    }

    private static void Write(string path, IReadOnlyList<HostDto> snapshot)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new HostFile { Hosts = new List<HostDto>(snapshot) }, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private static ServerEndpoint ToEndpoint(HostDto host)
        // The service GUID is Lantern's own and needs no storing; an old file cannot change which
        // protocol this build speaks.
        => new(string.IsNullOrWhiteSpace(host.Name) ? host.Address : host.Name, host.Address, LanternProtocol.ServiceId);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            saves.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // A save that will not finish is not worth throwing over on the way out.
        }
    }

    /// <summary>The file's shape: a list of names and addresses, nothing else.</summary>
    private sealed class HostFile
    {
        public int Version { get; set; } = 1;

        public List<HostDto> Hosts { get; set; } = [];
    }

    private sealed class HostDto
    {
        public string Name { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;
    }
}
