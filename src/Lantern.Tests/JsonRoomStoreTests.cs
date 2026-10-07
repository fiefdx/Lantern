using Lantern.Domain;
using Lantern.Storage;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T3.1: <see cref="JsonRoomStore"/> — the one file that has to survive everything, because F9 is the
/// requirement that says a bad file must never stop the program starting. Half of these tests are about
/// files that should not have worked.
/// </summary>
public sealed class JsonRoomStoreTests
{
    [Fact]
    public void AMissingFileIsTheDefaultRoomsRatherThanAFailure()
    {
        using var file = new TempRoomFile();

        using var store = new JsonRoomStore(file.Path);
        Assert.Equal(["Room 1", "Room 2", "Room 3"], store.Load().Select(r => r.Name));
    }

    [Fact]
    public async Task ARoomListSurvivesARestart()
    {
        using var file = new TempRoomFile();

        using (var store = new JsonRoomStore(file.Path))
        {
            await store.SaveAsync([new Room("Kitchen Table"), new Room("Garden"), new Room("Room 1")]);
        }

        // "Restored" is a new process, which in a test means a new store over the same bytes.
        using var reopened = new JsonRoomStore(file.Path);
        Assert.Equal(["Kitchen Table", "Garden", "Room 1"], reopened.Load().Select(r => r.Name));
    }

    [Fact]
    public async Task AReopenedRoomHasNobodyInItAndNothingToRead()
    {
        using var file = new TempRoomFile();

        using var store = new JsonRoomStore(file.Path);
        var room = new Room("Kitchen Table");
        room.AddMember(new User("conn-1", "Cara"));
        await store.SaveAsync([room]);

        var loaded = store.Load().Single();
        Assert.Empty(loaded.Members);
        Assert.Empty(loaded.Transcript);
    }

    [Fact]
    public async Task TheFileHoldsRoomNamesAndNothingElse()
    {
        using var file = new TempRoomFile();

        using var store = new JsonRoomStore(file.Path);
        var room = new Room("Kitchen Table");
        room.AddMember(new User("conn-1", "Cara"));
        room.Append(new TextMessage("Cara", "Kitchen Table", "the postcode is 9021"));
        await store.SaveAsync([room]);

        var written = await File.ReadAllTextAsync(file.Path);

        // F9, checked against the bytes rather than the object model: no history, no members, no ids.
        Assert.Contains("Kitchen Table", written);
        Assert.DoesNotContain("postcode", written);
        Assert.DoesNotContain("Cara", written);
        Assert.DoesNotContain("conn-1", written);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("{ this is not json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[1, 2, 3]")]
    [InlineData("[{\"title\":\"Room 1\"}]")]
    public void AFileThatIsNotARoomListIsTheDefaultRooms(string contents)
    {
        using var file = new TempRoomFile(contents);

        using var store = new JsonRoomStore(file.Path);
        Assert.Equal(["Room 1", "Room 2", "Room 3"], store.Load().Select(r => r.Name));
    }

    [Theory]
    [InlineData("""{"version": 1, "rooms": []}""")]
    [InlineData("[]")]
    public void ARoomListOfNothingMeansNoRoomsAndStaysThatWay(string contents)
    {
        // Different from a broken file. A host that deleted every room made a decision, and getting
        // "Room 1, Room 2, Room 3" back on the next start would be the program disagreeing with it.
        using var file = new TempRoomFile(contents);

        using var store = new JsonRoomStore(file.Path);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void AHandEditedArrayOfNamesIsUnderstood()
    {
        // The shape someone writes when they open the file in Notepad to check F9 - and the shape an
        // earlier build might have left behind.
        using var file = new TempRoomFile("""["Kitchen Table", "Garden"]""");

        using var store = new JsonRoomStore(file.Path);
        Assert.Equal(["Kitchen Table", "Garden"], store.Load().Select(r => r.Name));
    }

    [Fact]
    public void AUselessEntryIsDroppedAndTheRestOfTheFileIsKept()
    {
        using var file = new TempRoomFile("""
            { "rooms": [ { "name": "Kitchen Table" }, { "name": "   " }, { "name": "" }, { "name": 42 }, {} ] }
            """);

        using var store = new JsonRoomStore(file.Path);
        Assert.Equal(["Kitchen Table"], store.Load().Select(r => r.Name));
    }

    [Fact]
    public void TwoSpellingsOfTheSameRoomBecomeOneRoom()
    {
        using var file = new TempRoomFile("""{"rooms": [{"name": "Kitchen Table"}, {"name": "kitchen TABLE"}]}""");

        using var store = new JsonRoomStore(file.Path);

        // Room names are matched case-insensitively everywhere else, so the first spelling wins rather
        // than the host being offered two rooms that refuse the same joins differently.
        Assert.Equal(["Kitchen Table"], store.Load().Select(r => r.Name));
    }

    [Fact]
    public void ANameLongerThanTheRuleIsNotLoaded()
    {
        var tooLong = new string('R', ValidationRules.RoomNameMaxLength + 1);
        using var file = new TempRoomFile($$"""{"rooms": [{"name": "{{tooLong}}"}]}""");

        using var store = new JsonRoomStore(file.Path);

        // Nothing usable, and the file did list something: treated as junk, so the defaults come back.
        Assert.Equal(["Room 1", "Room 2", "Room 3"], store.Load().Select(r => r.Name));
    }

    [Fact]
    public async Task ASaveWritesValidJSONWithAVersionAndNoStrayTemporaryFile()
    {
        using var file = new TempRoomFile();

        using var store = new JsonRoomStore(file.Path);
        await store.SaveAsync([new Room("Kitchen Table")]);

        var written = await File.ReadAllTextAsync(file.Path);
        Assert.Contains("\"version\"", written);
        Assert.False(File.Exists(file.Path + ".tmp"));   // replaced in one move, not left behind
    }

    [Fact]
    public async Task ASaveThatCannotHappenIsReportedAndNotThrown()
    {
        using var directory = new TempDirectory();

        // A directory that cannot exist: the path runs through a plain file.
        var blocker = Path.Combine(directory.Path, "blocker");
        await File.WriteAllTextAsync(blocker, "not a directory");
        var path = Path.Combine(blocker, "rooms.json");

        using var store = new JsonRoomStore(path);
        var complaints = new List<string>();
        store.SaveFailed += (_, message) => complaints.Add(message);

        // The room list in memory is still right and the chat is still running; the host is told the
        // save did not land, which is all a program can honestly do about a disk it cannot write.
        await store.SaveAsync([new Room("Kitchen Table")]);

        Assert.Single(complaints);
        Assert.Contains("room list", complaints[0]);
    }

    [Fact]
    public async Task TwoSavesAtOnceDoNotInterleave()
    {
        using var file = new TempRoomFile();
        using var store = new JsonRoomStore(file.Path);

        var first = store.SaveAsync(Enumerable.Range(0, 20).Select(i => new Room($"Room {i}")));
        var second = store.SaveAsync([new Room("Only one")]);
        await Task.WhenAll(first, second);

        // Whatever wins, it wins whole: the file is one complete list and it still loads.
        var loaded = store.Load();
        Assert.NotEmpty(loaded);
        Assert.True(loaded.Count == 1 || loaded.Count == 20);
    }

    [Fact]
    public void TheDefaultStorePointsAtPerUserData()
    {
        // Not the program folder: that may sit under Program Files, where a normal user cannot write, and
        // a chat that cannot save its rooms is a chat that argues with the machine.
        var path = Path.Combine(JsonRoomStore.DefaultDirectory, JsonRoomStore.FileName);
        Assert.Contains("Lantern", path, StringComparison.Ordinal);
        Assert.EndsWith("rooms.json", path, StringComparison.Ordinal);
    }

    /// <summary>A file in the temporary folder, deleted when the test ends.</summary>
    private sealed class TempRoomFile : IDisposable
    {
        public TempRoomFile(string? contents = null)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lantern-rooms-{Guid.NewGuid():N}.json");

            if (contents is not null)
            {
                File.WriteAllText(Path, contents);
            }
        }

        public string Path { get; }

        public void Dispose()
        {
            foreach (var candidate in new[] { Path, Path + ".tmp" })
            {
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
        }
    }

    /// <summary>An empty directory, deleted with everything in it when the test ends.</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lantern-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // The temporary folder is cleaned up by the machine eventually.
            }
        }
    }
}
