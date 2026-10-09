using Lantern.Core;
using Lantern.Domain;
using Lantern.Storage;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T5.3 (and part of T5.6): a file across the wire in pieces (F8).
/// <para>
/// A picture fits in one frame and a file does not, and that difference is the whole test surface: the
/// bytes have to be reassembled in order, a transfer that stops part way must not quietly become a short
/// file, one person pressing Cancel has to stop the machine that is reading its disk, and none of it may
/// run the room out of memory or stop people talking while it happens.
/// </para>
/// <para>
/// Where a test needs a frame sequence that Lantern itself would never produce - a file that stops short,
/// a chunk numbered wrongly, more bytes than were promised - it writes the frames straight onto a link.
/// Those are the cases a peer of a different vintage, or a hostile one, would send, and the host is the
/// thing standing in front of the room.
/// </para>
/// </summary>
public sealed class FileTransferTests : IDisposable
{
    private readonly List<string> tempFiles = [];
    private readonly List<Guid> transfers = [];

    // ------------------------------------------------------- the ordinary case

    [Fact]
    public async Task AFileArrivesWholeInTheWindowsOfEverybodyElse()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        var hostFrames = WatchFrames(host);
        var offeredToCara = WatchJobs(cara);
        var offeredToDov = WatchJobs(dov);
        var seenByCara = WatchMessages(cara);
        var seenByDov = WatchMessages(dov);

        // Five chunks of 8 KiB, the last one part full. A reassembly bug almost always lives in that last
        // partial chunk, so a file that divides exactly would be the least informative thing to send.
        var payload = Bytes(40_000);
        var job = server.SendFile(host, TempFile("notes.zip", payload));

        await FinishedAsync(job);

        Assert.True(job.IsComplete);
        Assert.Equal(payload.LongLength, job.BytesDone);

        var fromCara = await offeredToCara.ItemAsync(j => j.FileName == "notes.zip", "Cara to be offered the file");
        var fromDov = await offeredToDov.ItemAsync(j => j.FileName == "notes.zip", "Dov to be offered the file");

        Assert.Equal("Hosty", fromCara.SenderNickname);
        Assert.Equal("Room 1", fromCara.RoomName);
        Assert.Equal(payload.LongLength, fromCara.SizeBytes);

        // One event, seen twice: the same transfer and the same transcript line in both windows. If the
        // host minted ids per recipient, four windows would show four files that happen to match.
        Assert.Equal(job.Id, fromCara.Id);
        Assert.Equal(job.Id, fromDov.Id);
        Assert.Equal(job.MessageId, fromCara.MessageId);
        Assert.Equal(fromCara.MessageId, fromDov.MessageId);

        var lineCara = await seenByCara.ItemAsync(m => m is FileMessage, "Cara's transcript line");
        var lineDov = await seenByDov.ItemAsync(m => m is FileMessage, "Dov's transcript line");
        Assert.Equal(job.MessageId, lineCara.Id);
        Assert.Equal(lineCara.Id, lineDov.Id);
        Assert.Equal(job.Id, ((FileMessage)lineCara).TransferId);

        var caraPutIt = TempPath("cara-notes.zip");
        var dovPutIt = TempPath("dov-notes.zip");

        await SaveAsync(cara, fromCara, caraPutIt);
        await SaveAsync(dov, fromDov, dovPutIt);

        Assert.Equal(payload, await File.ReadAllBytesAsync(caraPutIt));
        Assert.Equal(payload, await File.ReadAllBytesAsync(dovPutIt));

        // Each window is offered it once. A second copy would not be a second file, it would be the same
        // transfer spooled twice.
        Assert.Single(offeredToCara.Items);
        Assert.Single(offeredToDov.Items);

        // And nobody is handed their own file back: the host sent this one, so its own copy of the room is
        // not offered it, which is the same rule a text message follows.
        Assert.Equal(0, hostFrames.CountMatch(f => f.Type == FrameType.FILE_META));
    }

    [Fact]
    public async Task AFileFromAClientsWindowReachesTheHostAndTheOtherClient()
    {
        await using var server = await StartAsync();
        await using var service = new HostChatService(server, "Hosty");
        await service.StartAsync();
        await service.JoinByNameAsync("Room 1");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        var offeredToHost = WatchJobs(service);
        var offeredToDov = WatchJobs(dov);

        var payload = Bytes(9_000);
        var job = cara.SendFile(TempFile("deck.pdf", payload));

        await FinishedAsync(job);

        // The host is a member of its own room, so it is offered the file too, on the same terms and with
        // the same progress a client gets. Nothing about receiving is special-cased for the host window.
        var forHost = await offeredToHost.ItemAsync(j => j.FileName == "deck.pdf", "the host's window to be offered the file");
        var forDov = await offeredToDov.ItemAsync(j => j.FileName == "deck.pdf", "Dov to be offered the file");

        Assert.Equal("Cara", forHost.SenderNickname);
        Assert.Equal("Cara", forDov.SenderNickname);

        var hostPutIt = TempPath("host-deck.pdf");
        await SaveAsync(service, forHost, hostPutIt);
        Assert.Equal(payload, await File.ReadAllBytesAsync(hostPutIt));

        var dovPutIt = TempPath("dov-deck.pdf");
        await SaveAsync(dov, forDov, dovPutIt);
        Assert.Equal(payload, await File.ReadAllBytesAsync(dovPutIt));
    }

    [Fact]
    public async Task SingleByteChunksAreReassembledIntoTheSameFile()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offeredToDov = WatchJobs(dov);

        // One byte per chunk, in order, for a file small enough to check byte by byte afterwards. The
        // receiver is not allowed to assume a chunk is a particular size or that the first one is whole:
        // it is appending to a file, not counting frames.
        byte[] payload = [.. Enumerable.Range(0, 200).Select(i => (byte)(i * 7 % 251))];

        var id = Guid.NewGuid();
        transfers.Add(id);

        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Zed"), Frame.JoinRoom(server.Id("Room 1")));

        var frames = new List<Frame> { Frame.FileMeta(Guid.NewGuid(), id, server.Id("Room 1"), "Room 1", "Zed", "tiny.bin", payload.LongLength) };

        for (var i = 0; i < payload.Length; i++)
        {
            frames.Add(Frame.FileChunk(id, i, [payload[i]]));
        }

        frames.Add(Frame.FileDone(id));
        await SendRawAsync(raw, [.. frames]);

        var job = await offeredToDov.ItemAsync(j => j.FileName == "tiny.bin", "Dov to be offered 200 one-byte chunks");
        Assert.Equal(200, job.SizeBytes);

        var saved = TempPath("tiny.bin");
        await SaveAsync(dov, job, saved);

        Assert.Equal(payload, await File.ReadAllBytesAsync(saved));
    }

    [Fact]
    public async Task AFileThatStoppedShortNeverBecomesAFile()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offeredToDov = WatchJobs(dov);

        var id = Guid.NewGuid();
        transfers.Add(id);

        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Zed"), Frame.JoinRoom(server.Id("Room 1")));

        // A hundred bytes promised, forty sent, and then "done". The host sees the count does not add up,
        // and the receiver must not be left holding something that looks like a saved file.
        await SendRawAsync(raw,
            Frame.FileMeta(Guid.NewGuid(), id, server.Id("Room 1"), "Room 1", "Zed", "half.bin", 100),
            Frame.FileChunk(id, 0, Bytes(40)),
            Frame.FileDone(id));

        var job = await offeredToDov.ItemAsync(j => j.FileName == "half.bin", "Dov to be offered the file");

        var saved = TempPath("half.bin");
        var problem = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(dov, job, saved));

        // Whichever half noticed - the host counting bytes, or this machine being told to stop - the answer
        // is a sentence naming the file and the fact that nothing arrived.
        Assert.Contains("half.bin", problem.Message);
        Assert.False(File.Exists(saved));

        // The job says what happened rather than sitting at 40% forever: a bar that has stopped moving is
        // not an answer, and this one is over.
        await Watch.UntilAsync(() => job.Finished.IsCompleted, "the short transfer to be over");
        Assert.True(job.Cancel.IsCancellationRequested);
        Assert.False(job.IsComplete);
    }

    [Fact]
    public async Task AChunkNumberMissingInTheMiddleIsNotAFile()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offeredToDov = WatchJobs(dov);

        var id = Guid.NewGuid();
        transfers.Add(id);

        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Zed"), Frame.JoinRoom(server.Id("Room 1")));

        // Chunks 0, 1 and 3: two of the three arrive and the file is not there. A receiver that appended
        // whatever turned up would save a file with a hole in it that opens as garbage and blames the
        // person who chose to keep it.
        await SendRawAsync(raw,
            Frame.FileMeta(Guid.NewGuid(), id, server.Id("Room 1"), "Room 1", "Zed", "gappy.bin", 60),
            Frame.FileChunk(id, 0, Bytes(20)),
            Frame.FileChunk(id, 1, Bytes(20)),
            Frame.FileChunk(id, 3, Bytes(20)),
            Frame.FileDone(id));

        var job = await offeredToDov.ItemAsync(j => j.FileName == "gappy.bin", "Dov to be offered the file");

        var saved = TempPath("gappy.bin");
        await Assert.ThrowsAnyAsync<Exception>(() => SaveAsync(dov, job, saved));
        Assert.False(File.Exists(saved));
        await Watch.UntilAsync(() => job.Finished.IsCompleted, "the transfer with the hole in it to be over");
    }

    [Fact]
    public async Task NobodyIsGivenMoreBytesThanWerePromised()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");
        var hostFrames = WatchFrames(host);

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offeredToDov = WatchJobs(dov);

        var id = Guid.NewGuid();
        transfers.Add(id);

        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Zed"), Frame.JoinRoom(server.Id("Room 1")));

        await SendRawAsync(raw,
            Frame.FileMeta(Guid.NewGuid(), id, server.Id("Room 1"), "Room 1", "Zed", "lying.bin", 10),
            Frame.FileChunk(id, 0, Bytes(30)));

        // The host is the one place that can see the lie, and it stops the transfer instead of letting
        // every receiver discover their own failure.
        var cancel = await hostFrames.ItemAsync(f => f.Type == FrameType.FILE_CANCEL, "the host to stop the transfer");
        Assert.Equal(id, cancel.Header.TransferId);

        // The person who sent it is told too: their half of the room has gone away.
        await ReadRawAsync(raw, FrameType.FILE_CANCEL);

        // The room was offered the file before the lie was found out - a size only becomes a lie when the
        // bytes say so - so Dov ends up with a job that stops rather than with nothing at all. What matters
        // is that it stops, says so, and leaves no file behind.
        var job = await offeredToDov.ItemAsync(j => j.FileName == "lying.bin", "Dov to be offered the file");

        await Watch.UntilAsync(() => job.Finished.IsCompleted, "the lie to end the transfer");
        Assert.True(job.Cancel.IsCancellationRequested);
        Assert.False(job.IsComplete);

        var saved = TempPath("lying.bin");
        await Assert.ThrowsAnyAsync<Exception>(() => SaveAsync(dov, job, saved));
        Assert.False(File.Exists(saved));
    }

    [Fact]
    public async Task BytesWithNoFileBehindThemDoNotBecomeAFile()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offeredToDov = WatchJobs(dov);

        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Zed"), Frame.JoinRoom(server.Id("Room 1")));

        // Chunks and a "done" for a transfer nobody announced. There is no name, no size and no sender to
        // put in a file, so making one up would be inventing a file out of nothing.
        var id = Guid.NewGuid();

        await SendRawAsync(raw,
            Frame.FileChunk(id, 0, Bytes(10)),
            Frame.FileDone(id));

        await Task.Delay(200);
        Assert.Empty(offeredToDov.Items);

        // And the connection survives: refusing one thing is not a reason to drop a person. The watcher is
        // listening before the message is sent - a message that arrived first would leave this waiting for
        // something that already happened, which is a race the test would win most days and lose in CI.
        var seenByText = WatchMessages(dov);

        await SendRawAsync(raw, Frame.Text(Guid.NewGuid(), server.RoomOrThrow("Room 1"), "Zed", "still here", DateTime.UtcNow));
        await seenByText.ItemAsync(m => m is TextMessage note && note.Body == "still here", "the room to still work");
    }

    [Theory]
    [InlineData(@"..\..\elsewhere\payload.bin")]
    [InlineData(@"C:\Windows\payload.bin")]
    [InlineData("two/dirs/deep/payload.bin")]
    [InlineData("   payload.bin   ")]
    public async Task ANameWithAPathInItArrivesAsAFileName(string announced)
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offeredToDov = WatchJobs(dov);

        var id = Guid.NewGuid();
        transfers.Add(id);

        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Zed"), Frame.JoinRoom(server.Id("Room 1")));

        await SendRawAsync(raw,
            Frame.FileMeta(Guid.NewGuid(), id, server.Id("Room 1"), "Room 1", "Zed", announced, 4),
            Frame.FileChunk(id, 0, Bytes(4)),
            Frame.FileDone(id));

        var offered = await offeredToDov.ItemAsync(j => j.SizeBytes == 4, "the file to arrive");

        // A file name is a label, not a path. A receiver that obeyed the path in an announcement would be
        // letting a stranger decide where its own disk gets written - and the window puts the file into the
        // folder the person chose by combining that folder with exactly this string, so the string has to be
        // one name before it ever gets that far.
        Assert.Equal("payload.bin", offered.FileName);

        var saved = TempPath("payload.bin");
        await SaveAsync(dov, offered, saved);
        Assert.Equal(4, new FileInfo(saved).Length);
    }

    [Fact]
    public async Task TheHostRefusesAFileOfAnImpossibleSize()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offeredToDov = WatchJobs(dov);

        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Zed"), Frame.JoinRoom(server.Id("Room 1")));

        // One frame can promise any size at all, and the price of believing it is paid out of somebody
        // else's disk. The ceiling is checked before the room is told, so the refusal is one sentence to
        // one person rather than a failed transfer in every window.
        await SendRawAsync(raw, Frame.FileMeta(Guid.NewGuid(), Guid.NewGuid(), server.Id("Room 1"), "Room 1", "Zed",
            "enormous.bin",
            LanternProtocol.MaxReceivedFileBytes + 1));

        var error = await ReadRawAsync(raw, FrameType.ERROR);
        Assert.Contains("does not carry files", error.Header.Error);

        await Task.Delay(150);
        Assert.Empty(offeredToDov.Items);
    }

    [Fact]
    public async Task AFileTooBigToBeWantedIsRefusedBeforeItStarts()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offeredToDov = WatchJobs(dov);

        // Half a gigabyte plus one byte, made as a file with a length and no contents: NTFS grants that in
        // one metadata call, which is what makes a size ceiling testable without writing anything.
        var huge = TempPath("huge.bin");

        tempFiles.Add(huge);

        await using (var created = File.Create(huge))
        {
            created.SetLength(LanternProtocol.MaxReceivedFileBytes + 1);
        }

        var problem = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => dov.SendFile(huge)));
        Assert.Contains("does not send files over", problem.Message);

        await Task.Delay(150);
        Assert.Empty(offeredToDov.Items);
    }

    // ------------------------------------------------------------- cancelling

    [Fact]
    public async Task CancellingStopsTheMachineSendingTheFile()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 2");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        // Dov is the only person who wants this file, so when he stops wanting it the transfer is over -
        // and "over" has to reach the machine reading its own disk, not just the window that pressed the
        // button. Eight megabytes is far more than either of them will have moved by then.
        dov.IncomingFile += (_, job) => job.CancelTransfer();

        var payload = Bytes(8 * 1024 * 1024);
        var job = cara.SendFile(TempFile("film.mp4", payload));

        await FinishedAsync(job);

        Assert.True(job.Cancel.IsCancellationRequested);
        Assert.False(job.IsComplete);
        Assert.True(job.BytesDone < payload.LongLength, $"the sender moved all {job.BytesDone:N0} bytes of a file nobody wanted");

        // Nothing is left on Dov's machine: it was declined, and a declined file should not be found in a
        // folder three weeks later with nobody remembering agreeing to it.
        Assert.False(File.Exists(SpoolPath(job.Id)));
    }

    [Fact]
    public async Task OnePersonDecliningDoesNotStopTheFileForTheOthers()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        var offeredToCara = WatchJobs(cara);
        var offeredToDov = WatchJobs(dov);

        // Two people are offered the same file and one of them does not want it. That is their business,
        // not the room's: the other still gets every byte. One "no" is not a vote that ends a transfer.
        dov.IncomingFile += (_, job) => job.CancelTransfer();

        var payload = Bytes(30_000);
        var job = server.SendFile(host, TempFile("photo-batch.zip", payload));

        await FinishedAsync(job);

        var forCara = await offeredToCara.ItemAsync(j => j.FileName == "photo-batch.zip", "Cara to be offered the file");

        var saved = TempPath("photo-batch.zip");
        await SaveAsync(cara, forCara, saved);
        Assert.Equal(payload, await File.ReadAllBytesAsync(saved));
    }

    [Fact]
    public async Task ATransferNobodyWantsIsStoppedAtTheSource()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 2");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        // Two clients, one file, nobody in the room but them - so Dov declining leaves nobody who wants
        // it, and the host must stop relaying rather than queue a whole file for an empty room.
        var offeredToDov = WatchJobs(dov);

        dov.IncomingFile += (_, job) => job.CancelTransfer();

        var job = cara.SendFile(TempFile("nobody.bin", Bytes(60_000)));

        await FinishedAsync(job);

        await Watch.UntilAsync(() => job.Cancel.IsCancellationRequested, "the host to stop the transfer");
        Assert.True(job.BytesDone < 60_000, "the whole file was pushed at a room that had stopped wanting it");
        Assert.Single(offeredToDov.Items);
    }

    // ------------------------------------------------------------- the room

    [Fact]
    public async Task TheRoomKeepsTalkingWhileAFileIsOnItsWay()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        var seenByDov = WatchMessages(dov);

        // Four megabytes is 512 chunks, which is plenty for a sentence to be lost behind if the host
        // queued a whole file in front of everything else in the room.
        var job = server.SendFile(host, TempFile("album.zip", Bytes(4 * 1024 * 1024)));

        var typed = DateTime.UtcNow;
        await cara.SendAsync(new TextMessage("Cara", "Room 1", "while you are at it", DateTime.UtcNow));

        var arrived = await seenByDov.ItemAsync(m => m is TextMessage said && said.Body == "while you are at it", "a sentence typed during a file transfer");
        Assert.True(arrived.SentAt >= typed.AddSeconds(-5));

        await FinishedAsync(job);
        Assert.True(job.IsComplete);
    }

    [Fact]
    public async Task TwoFilesCrossingAtTheSameTimeBothArriveWhole()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 2");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await using var ellen = new ChatClient("Ellen");
        await JoinAllAsync(server, [cara, dov, ellen], "Room 1");

        var offeredToEllen = WatchJobs(ellen);

        // Two people sending at once, into a room with a third person in it. Each transfer has its own
        // queue and its own numbers, so an implementation that shared one buffer would interleave two
        // files into one that matches neither.
        var caraSends = cara.SendFile(TempFile("cara.zip", Bytes(70_000)));
        var dovSends = dov.SendFile(TempFile("dov.zip", Bytes(90_000)));

        await Task.WhenAll(caraSends.Finished, dovSends.Finished);

        await offeredToEllen.UntilAsync(items => items.Count >= 2, "both files to reach Ellen");

        var names = offeredToEllen.Items.Select(j => j.FileName).Order().ToArray();
        Assert.Equal(["cara.zip", "dov.zip"], names);

        foreach (var job in offeredToEllen.Items)
        {
            var saved = TempPath(job.FileName);
            await SaveAsync(ellen, job, saved);

            var expected = job.FileName == "cara.zip" ? 70_000 : 90_000;
            Assert.Equal(expected, (await File.ReadAllBytesAsync(saved)).LongLength);
        }
    }

    [Fact]
    public async Task AFileOfRealSizeCrossesTheRoom()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        var offeredToCara = WatchJobs(cara);
        var offeredToDov = WatchJobs(dov);

        // The scale the milestone asks for: a dozen megabytes from the host to two clients. Everything
        // else here is small enough to reason about; this is small enough to be plausible and big enough
        // that a whole-file-in-memory mistake shows up as a heap spike rather than as a passing test.
        var payload = Bytes(12 * 1024 * 1024);
        var job = server.SendFile(host, TempFile("field-recording.wav", payload));

        await FinishedAsync(job, TimeSpan.FromMinutes(2));
        Assert.True(job.IsComplete);

        foreach (var (client, watcher, who) in new[] { (cara, offeredToCara, "Cara"), (dov, offeredToDov, "Dov") })
        {
            var offered = await watcher.ItemAsync(j => j.FileName == "field-recording.wav", $"{who} to be offered a dozen megabytes");
            Assert.Equal(payload.LongLength, offered.SizeBytes);

            var saved = TempPath($"{who}-field-recording.wav");
            await SaveAsync(client, offered, saved);

            // Checked by length rather than by comparing twelve megabyte arrays: what is being asserted is
            // that nothing was lost and nothing was duplicated, and a hash of a known pattern would only
            // be a slower way of saying the same.
            Assert.Equal(payload.LongLength, new FileInfo(saved).Length);
            Assert.False(File.Exists(SpoolPath(offered.Id)));
        }
    }

    // --------------------------------------------------------- where it lands

    [Fact]
    public async Task AFileIsNotSomewhereOnThisMachineUntilAPersonSaysWhere()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offeredToDov = WatchJobs(dov);

        var payload = Bytes(5_000);
        var job = server.SendFile(host, TempFile("report.docx", payload));

        await FinishedAsync(job);

        var offered = await offeredToDov.ItemAsync(j => j.FileName == "report.docx", "Dov to be offered the file");

        // The bytes have arrived but nobody has been asked yet, so there is no folder of his that this is
        // sitting in - it is in Lantern's own area, and it stays there until he chooses (F8: pick a folder).
        var wanted = TempPath("report.docx");
        Assert.False(File.Exists(wanted));

        await SaveAsync(dov, offered, wanted);
        Assert.True(File.Exists(wanted));
        Assert.Equal(payload, await File.ReadAllBytesAsync(wanted));
    }

    [Fact]
    public async Task AFileIsNotQuietlyWrittenOverTheTopOfAnotherOne()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offeredToDov = WatchJobs(dov);

        var job = server.SendFile(host, TempFile("minutes.txt", Bytes(2_000)));
        await FinishedAsync(job);

        var offered = await offeredToDov.ItemAsync(j => j.FileName == "minutes.txt", "Dov to be offered the file");

        // He already has a minutes.txt, and it is not this one. Replacing it because a file happened to
        // arrive is how people lose things.
        var existing = TempFile("minutes.txt", "the version he wrote himself"u8.ToArray());

        var problem = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(dov, offered, existing));
        Assert.Contains("already there", problem.Message);

        Assert.Equal("the version he wrote himself"u8.ToArray(), await File.ReadAllBytesAsync(existing));
    }

    [Fact]
    public async Task AFolderThatWillNotTakeTheFileIsReportedRatherThanCrashing()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offeredToDov = WatchJobs(dov);

        var job = server.SendFile(host, TempFile("holiday.jpg", Bytes(3_000)));
        await FinishedAsync(job);

        var offered = await offeredToDov.ItemAsync(j => j.FileName == "holiday.jpg", "Dov to be offered the file");

        // A path Windows will not accept, which is what "somewhere I cannot write" looks like from here:
        // a read-only share, a drive that has gone, a name with a character in it. The file is still in
        // Lantern's own area, so he can be told and asked again.
        var impossible = Path.Combine(Path.GetDirectoryName(TempPath("x"))!, "not:valid?.jpg");

        var problem = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(dov, offered, impossible));
        Assert.Contains("could not be saved there", problem.Message);

        // The file is still there to be saved somewhere sensible.
        var sensible = TempPath("holiday.jpg");
        await SaveAsync(dov, offered, sensible);
        Assert.Equal(3_000, new FileInfo(sensible).Length);
    }

    [Fact]
    public async Task TheHostsWindowDoesNotSendAFileAsAMessage()
    {
        await using var server = await StartAsync();
        await using var service = new HostChatService(server, "Hosty");
        await service.StartAsync();
        await service.JoinByNameAsync("Room 1");

        // A FileMessage on its own says "a file is coming" without any way to send it, so building one and
        // calling Send is a mistake rather than a feature. Saying so beats a transfer that never starts.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync(new FileMessage("Hosty", "Room 1", "notes.pdf", 12)));

        // The real way works and announces it to the room.
        var job = service.SendFile(TempFile("notes.pdf", Bytes(120)));
        await FinishedAsync(job);
        Assert.True(job.IsComplete);
    }

    [Fact]
    public async Task AFileThatIsNotThereIsNotStarted()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var offeredToDov = WatchJobs(dov);

        // Chosen from a list, then deleted, on a USB stick, by somebody else. The window needs a sentence
        // rather than a transfer that stalls at zero.
        await Assert.ThrowsAsync<FileNotFoundException>(() => Task.Run(() => dov.SendFile(Path.Combine(Path.GetTempPath(), "lantern-absent", "gone.bin"))));

        await Task.Delay(150);
        Assert.Empty(offeredToDov.Items);
    }

    // ------------------------------------------------------------- framing

    [Fact]
    public async Task AFileIsFramedAsOneAnnouncementThenEveryChunkThenOneClose()
    {
        var payload = Bytes((FrameCodec.ChunkBytes * 2) + 7);
        var path = TempFile("framing.bin", payload);
        var written = new List<Frame>();

        var job = new FileTransferJob("framing.bin", payload.LongLength, senderNickname: "Cara", roomName: "Room 1");
        var wentWhole = await FileSending.StreamAsync(job, path, frame =>
        {
            written.Add(frame);
            return Task.CompletedTask;
        });

        Assert.True(wentWhole);

        Assert.Equal(FrameType.FILE_META, written[0].Type);
        Assert.Equal(FrameType.FILE_DONE, written[^1].Type);
        Assert.Equal(job.MessageId, written[0].Header.Id);
        Assert.Equal(job.Id, written[0].Header.TransferId);
        Assert.Equal(payload.LongLength, written[0].Header.SizeBytes);

        var chunks = written.Skip(1).SkipLast(1).ToArray();

        // Three chunks for two and a bit: the bytes go out whole, in order, numbered from zero, and the
        // last one is allowed to be short. That is the whole contract a receiver has to rely on.
        Assert.Equal(3, chunks.Length);
        Assert.All(chunks, frame => Assert.Equal(FrameType.FILE_CHUNK, frame.Type));
        Assert.Equal([0, 1, 2], chunks.Select(f => f.Header.Sequence));
        Assert.Equal([FrameCodec.ChunkBytes, FrameCodec.ChunkBytes, 7], chunks.Select(f => f.Body.Length));
        Assert.Equal(payload, chunks.SelectMany(f => f.Body).ToArray());

        // Progress is counted as the bytes leave, so a bar at 100% means the room has them.
        Assert.Equal(1.0, job.Fraction);
    }

    [Fact]
    public async Task AStoppedFileSaysSoInsteadOfLeavingTheRoomWaiting()
    {
        var path = TempFile("stopme.bin", Bytes(FrameCodec.ChunkBytes * 4));
        var written = new List<Frame>();

        var job = new FileTransferJob("stopme.bin", 4 * FrameCodec.ChunkBytes, senderNickname: "Cara", roomName: "Room 1");

        // Cancelled part way through, on the third chunk. Without the last of these the receiver cannot
        // tell a slow transfer from a stopped one and keeps a half file and a bar that has stopped moving.
        var seen = 0;

        var wentWhole = await FileSending.StreamAsync(job, path, frame =>
        {
            written.Add(frame);

            if (++seen == 3)
            {
                job.CancelTransfer();
            }

            return Task.CompletedTask;
        });

        Assert.False(wentWhole);
        Assert.Equal(FrameType.FILE_META, written[0].Type);
        Assert.Equal(FrameType.FILE_CANCEL, written[^1].Type);
        Assert.DoesNotContain(written, f => f.Type == FrameType.FILE_DONE);
        Assert.True(job.Cancel.IsCancellationRequested);
    }

    [Fact]
    public async Task AnEmptyFileIsAFileToo()
    {
        var path = TempFile("empty.txt", []);
        var written = new List<Frame>();

        var job = new FileTransferJob("empty.txt", 0, senderNickname: "Cara", roomName: "Room 1");
        Assert.True(await FileSending.StreamAsync(job, path, frame =>
        {
            written.Add(frame);
            return Task.CompletedTask;
        }));

        // Zero chunks: an announcement and a close, and nothing in between. A receiver that waited for at
        // least one chunk would hang on a zero-byte file, which is a real file people send.
        Assert.Equal([FrameType.FILE_META, FrameType.FILE_DONE], written.Select(f => f.Type));
        Assert.True(job.IsComplete);
    }

    // ------------------------------------------------------------------ harness

    /// <summary>Ends the test, not the run: a transfer that never finishes is a failure with a name.</summary>
    private static async Task FinishedAsync(FileTransferJob job, TimeSpan? within = null)
    {
        var limit = within ?? TimeSpan.FromMilliseconds(Watch.DefaultTimeoutMs * 2L);

        var finished = await Task.WhenAny(job.Finished, Task.Delay(limit));

        if (finished != job.Finished)
        {
            throw new TimeoutException($"the transfer of {job.FileName} was still going after {limit.TotalSeconds:N0}s ({job.BytesDone:N0} of {job.SizeBytes:N0} bytes)");
        }
    }

    private static async Task SaveAsync(IChatService service, FileTransferJob job, string path)
    {
        using var cancel = new CancellationTokenSource(Watch.DefaultTimeoutMs * 2);
        await service.SaveReceivedFileAsync(job, path, cancel.Token);
    }

    /// <summary>Where Lantern keeps the bytes of a file that is arriving, before anybody has chosen a folder.</summary>
    private static string SpoolPath(Guid transferId)
        => Path.Combine(ReceivedPayloads.IncomingDirectory, $"{transferId:N}.part");

    /// <summary>Everything this test was offered, in the order it was offered (F8).</summary>
    private static Watch<FileTransferJob> WatchJobs(IChatService service)
    {
        var watch = new Watch<FileTransferJob>();
        service.IncomingFile += (_, job) => watch.Add(job);
        return watch;
    }

    private static byte[] Bytes(int count)
    {
        var bytes = new byte[count];

        // A pattern rather than zeroes, so that a chunk written twice or skipped shows up as a mismatch
        // instead of as a plausible run of nothing.
        for (var i = 0; i < count; i++)
        {
            bytes[i] = (byte)(i * 31 + (i >> 8) * 7);
        }

        return bytes;
    }

    /// <summary>
    /// A path for a file this test names. Each name gets a folder of its own so the file keeps the name it
    /// is announced to the room with: a transfer for "cara.zip" that arrives as
    /// "0b1c...-cara.zip" tests the wrong thing, and the first version of this harness did exactly that.
    /// </summary>
    private string TempPath(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lantern-file-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        return Path.Combine(folder, name);
    }

    private string TempFile(string name, byte[] contents)
    {
        var path = TempPath(name);
        File.WriteAllBytes(path, contents);
        tempFiles.Add(path);
        return path;
    }

    private static async Task<ChatServer> StartAsync()
    {
        var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host");
        await server.StartAsync();
        return server;
    }

    private static async Task<LocalParticipant> HostJoinsAsync(ChatServer server, string roomName)
    {
        var local = new LocalParticipant("Hosty");
        Assert.True(server.Register(local));
        server.JoinByName(local, roomName);
        await Task.CompletedTask;
        return local;
    }

    private static async Task JoinAllAsync(ChatServer server, IEnumerable<ChatClient> clients, string roomName)
    {
        foreach (var client in clients)
        {
            await client.ConnectAsync(server.Endpoint);
            await client.JoinByNameWhenShownAsync(roomName);
        }

        await Watch.UntilAsync(
            () => clients.All(c => c.CurrentRoomName == roomName),
            $"the joins to reach {roomName}");
    }

    private static async Task SendRawAsync(IClientLink link, params Frame[] frames)
    {
        foreach (var frame in frames)
        {
            await FrameCodec.WriteAsync(link.Output, frame);
        }

        await link.Output.FlushAsync();
    }

    private static async Task<Frame> ReadRawAsync(IClientLink link, FrameType wanted)
    {
        using var cancel = new CancellationTokenSource(Watch.DefaultTimeoutMs);

        while (true)
        {
            var frame = await FrameCodec.ReadAsync(link.Input, cancel.Token);

            if (frame is null)
            {
                throw new TimeoutException($"the connection ended before a {wanted} frame arrived");
            }

            if (frame.Type == wanted)
            {
                return frame;
            }
        }
    }

    private static Watch<Frame> WatchFrames(LocalParticipant participant)
    {
        var watch = new Watch<Frame>();
        participant.FrameReceived += (_, frame) => watch.Add(frame);
        return watch;
    }

    private static Watch<Message> WatchMessages(ChatClient client)
    {
        var watch = new Watch<Message>();
        client.MessageReceived += (_, message) => watch.Add(message);
        return watch;
    }

    public void Dispose()
    {
        foreach (var file in tempFiles.Where(File.Exists))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Temp files, in a temp folder, at the end of a test run.
            }
        }

        // Only the spools this test created: other tests are running in the same folder at the same time,
        // and their files are their own business.
        foreach (var id in transfers)
        {
            try
            {
                if (File.Exists(SpoolPath(id)))
                {
                    File.Delete(SpoolPath(id));
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
