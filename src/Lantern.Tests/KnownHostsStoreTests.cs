using Lantern.Core;
using Lantern.Storage;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T4.3: <see cref="JsonKnownHostsStore"/>. The reason this file exists is a measurement, not a feature
/// request: a Windows host is paired but not discoverable (T4.1), so the machines worth offering in the
/// join list are mostly the ones this laptop has joined before. Which makes it the second file that has
/// to survive a bad read — and, like the rooms file, it may never stop the program.
/// </summary>
public sealed class KnownHostsStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lantern-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void AFirstRunHasNoHistoryRatherThanAnError()
    {
        using var store = NewStore();

        Assert.Empty(store.Recent());
        Assert.False(System.IO.File.Exists(store.FilePath));     // nothing has been joined yet, so nothing is written
    }

    [Fact]
    public async Task AJoinIsRememberedAcrossTheWindowClosingAndTheProcessWithIt()
    {
        var file = FreshPath();

        using (var store = new JsonKnownHostsStore(file))
        {
            store.Remember(Endpoint("haitao-iPhone", "AC86A3294B28"));
            await store.SavesCompletedAsync();
        }

        // A new process, which in a test means a new store over the same bytes.
        using var reopened = new JsonKnownHostsStore(file);
        var remembered = Assert.Single(reopened.Recent());

        Assert.Equal("AC86A3294B28", remembered.DeviceAddress);
        Assert.Equal("haitao-iPhone", remembered.ServerName);

        // The service GUID is this build's business, so it is not stored and cannot be stale.
        Assert.Equal(LanternProtocol.ServiceId, remembered.ServiceId);
    }

    [Fact]
    public async Task TheSameMachineJoinedTwiceIsListedOnceAndAtTheFront()
    {
        using var store = NewStore();

        store.Remember(Endpoint("haitao-iPhone", "AC86A3294B28"));
        store.Remember(Endpoint("Kitchen Table", "34885DBDCC01"));
        store.Remember(Endpoint("iPhone of Haitao", "ac:86:a3:29:4b:28"));   // same machine, other shape
        await store.SavesCompletedAsync();

        var recent = store.Recent();

        // Two shapes of one address must not become two lines, and a typo fixed in the name must not
        // become two lines either; the join just happened, so it goes first.
        Assert.Equal("AC86A3294B28", Assert.Single(recent, h => h.DeviceAddress == "AC86A3294B28").DeviceAddress);
        Assert.Equal(2, recent.Count);
        Assert.Equal("iPhone of Haitao", recent[0].ServerName);
    }

    [Fact]
    public async Task OnlyTheLastTenMachinesAreRemembered()
    {
        using var store = NewStore();

        const int Joined = JsonKnownHostsStore.Limit + 4;

        for (var i = 0; i < Joined; i++)
        {
            store.Remember(Endpoint($"laptop {i}", i.ToString("X12")));
        }

        await store.SavesCompletedAsync();
        var recent = store.Recent();

        // A term's worth of project partners is a list; everything a person has ever sat next to is a
        // database, and this is a two-window chat program.
        Assert.Equal(JsonKnownHostsStore.Limit, recent.Count);
        Assert.Equal($"laptop {Joined - 1}", recent[0].ServerName);
        Assert.Equal((Joined - JsonKnownHostsStore.Limit).ToString("X12"), recent[^1].DeviceAddress);
    }

    [Fact]
    public async Task AHostThatNeverToldItsNameIsRememberedByItsAddress()
    {
        using var store = NewStore();

        // A typed join has no name to remember, and a blank line in the join list is not clickable.
        store.Remember(Endpoint("F8E4E3F9603B", "F8E4E3F9603B"));
        await store.SavesCompletedAsync();

        Assert.Equal("F8E4E3F9603B", Assert.Single(store.Recent()).ServerName);
    }

    [Fact]
    public async Task AHistoryFileNobodyCanReadIsNoHistoryRatherThanACrash()
    {
        var file = FreshPath();
        await File.WriteAllTextAsync(file, "{ this is not json, it is a shoebox ");

        using var store = new JsonKnownHostsStore(file);

        Assert.Empty(store.Recent());       // F9's rule again: a bad file is an empty list, never an exception

        store.Remember(Endpoint("haitao-iPhone", "AC86A3294B28"));
        await store.SavesCompletedAsync();

        // Being unable to read the past must not stop the present being written over it.
        using var reopened = new JsonKnownHostsStore(file);
        Assert.Single(reopened.Recent());
    }

    [Fact]
    public async Task EntriesThatAreNotMachinesAreLeftOutOnTheWayIn()
    {
        var file = FreshPath();
        await File.WriteAllTextAsync(file, """
            {
              "version": 1,
              "hosts": [
                { "name": "no address", "address": "   " },
                { "name": "not an address", "address": "A" },
                { "name": "a port nobody dials", "address": "laptop:99999" },
                { "name": "", "address": "" },
                { "name": "development", "address": "127.0.0.1:43797" },
                { "name": "fine", "address": "34885DBDCC01" }
              ]
            }
            """);

        using var store = new JsonKnownHostsStore(file);

        // Nothing that cannot be dialled belongs in the join list, whatever shape the file happens to be
        // in - and a loopback entry stays, because that is the development machine's own history.
        Assert.Equal(new[] { "development", "fine" }, store.Recent().Select(h => h.ServerName));
    }

    [Fact]
    public async Task AWriteThatCannotHappenIsReportedRatherThanThrown()
    {
        // A directory that cannot be made: a file is in the way. This is what a full disk or a
        // roaming-profile quirk looks like from here.
        var blocker = Path.Combine(root, "blocker");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(blocker, "a file where a directory should be");

        var store = new JsonKnownHostsStore(Path.Combine(blocker, "known-hosts.json"));

        string? complaint = null;
        store.SaveFailed += (_, message) => complaint = message;

        store.Remember(Endpoint("haitao-iPhone", "AC86A3294B28"));      // must not throw: the join is already done
        await store.SavesCompletedAsync();

        Assert.False(string.IsNullOrWhiteSpace(complaint));
        Assert.Single(store.Recent());                                  // and this session still has its history

        store.Dispose();
    }

    [Fact]
    public async Task WaitingForTheSavesIsWaitingForAllOfThem()
    {
        using var store = NewStore();

        store.Remember(Endpoint("one", "AAAAAAAAAAAA"));
        store.Remember(Endpoint("two", "BBBBBBBBBBBB"));
        store.Remember(Endpoint("three", "CCCCCCCCCCCC"));
        await store.SavesCompletedAsync();

        // Quitting the app must not lose the last join because its write was still queued behind two
        // others (the same discipline as the rooms file, T3.2).
        using var reopened = new JsonKnownHostsStore(store.FilePath);
        Assert.Equal(3, reopened.Recent().Count);
    }

    [Fact]
    public void RememberingNothingIsNotAnEntry()
    {
        using var store = NewStore();

        store.Remember(new ServerEndpoint("who knows", "   ", LanternProtocol.ServiceId));

        Assert.Empty(store.Recent());
    }

    [Fact]
    public void AnEndpointWithoutAnAddressIsRefusedOutLoud()
    {
        Assert.Throws<ArgumentNullException>(() => NewStore().Remember(null!));
    }

    private JsonKnownHostsStore NewStore() => new(FreshPath());

    private string FreshPath()
    {
        Directory.CreateDirectory(root);
        return Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
    }

    private static ServerEndpoint Endpoint(string name, string address)
        => new(name, address, LanternProtocol.ServiceId);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temp directory left behind is not worth failing a test over.
        }
    }
}
