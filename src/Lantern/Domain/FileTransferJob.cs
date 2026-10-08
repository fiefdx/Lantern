namespace Lantern.Domain;

/// <summary>
/// One in-flight file transfer (Figure 7). Drives FILE_META / FILE_CHUNK / FILE_CHUNK frames
/// and reports progress to the UI (F8).
/// </summary>
public sealed class FileTransferJob
{
    private long bytesDone;
    private readonly TaskCompletionSource<bool> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FileTransferJob(string fileName, long sizeBytes, Guid? id = null, string? senderNickname = null, string? roomName = null, Guid? messageId = null)
    {
        FileName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName)
            ? throw new ArgumentException("File name is required.", nameof(fileName))
            : fileName);

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        }

        SizeBytes = sizeBytes;
        Id = id ?? Guid.NewGuid();

        // FILE_META carries two ids: one for the transfer and one for the line in the transcript. They are
        // different things - the line is what a person sees, the transfer is the bytes behind it - and every
        // window in the room has to agree on both, so the sender mints them once and the frame carries them.
        MessageId = messageId ?? Guid.NewGuid();
        SenderNickname = senderNickname ?? string.Empty;
        RoomName = roomName ?? string.Empty;
        Cancel = new CancellationTokenSource();
    }

    public Guid Id { get; }

    /// <summary>Id of the transcript entry this transfer announces, as it appears in every window (F8).</summary>
    public Guid MessageId { get; }

    /// <summary>File name only — never a full path from the peer.</summary>
    public string FileName { get; }

    public long SizeBytes { get; }

    public string SenderNickname { get; }

    public string RoomName { get; }

    /// <summary>Set by the owner so progress can be marshaled to the UI thread.</summary>
    public IProgress<double>? Progress { get; set; }

    public CancellationTokenSource Cancel { get; }

    public CancellationToken CancellationToken => Cancel.Token;

    public long BytesDone
    {
        get => Interlocked.Read(ref bytesDone);
        private set => Interlocked.Exchange(ref bytesDone, value);
    }

    /// <summary>0..1; always 1 for a zero-length file, which is complete before it starts.</summary>
    public double Fraction
        => SizeBytes == 0 ? 1 : Math.Clamp((double)BytesDone / SizeBytes, 0, 1);

    public bool IsComplete => BytesDone >= SizeBytes;

    /// <summary>
    /// Completes when the transfer is over, however it ended - whole, stopped, or refused. Whoever is
    /// moving the bytes calls <see cref="Finish"/>; everybody else waits on this rather than guessing from
    /// the progress bar, which cannot tell "finished" from "stuck at the same number".
    /// </summary>
    public Task Finished => finished.Task;

    /// <summary>Adds bytes and pushes the new fraction through <see cref="Progress"/>.</summary>
    public void AddBytes(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        BytesDone = Math.Min(SizeBytes, BytesDone + count);
        Progress?.Report(Fraction);
    }

    /// <summary>
    /// Marks the transfer over. Called by whoever is moving the bytes - a client's send loop, the host's
    /// pump, a receiver that has saved or lost the file - and safe to call twice.
    /// </summary>
    internal void Finish() => finished.TrySetResult(true);

    public void CancelTransfer()
    {
        if (!Cancel.IsCancellationRequested)
        {
            Cancel.Cancel();
        }
    }

    public override string ToString() => $"{FileName} {Fraction:P0}";
}
