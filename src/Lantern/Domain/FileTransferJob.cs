namespace Lantern.Domain;

/// <summary>
/// One in-flight file transfer (Figure 7). Drives FILE_META / FILE_CHUNK / FILE_CHUNK frames
/// and reports progress to the UI (F8).
/// </summary>
public sealed class FileTransferJob
{
    private long bytesDone;

    public FileTransferJob(string fileName, long sizeBytes, Guid? id = null, string? senderNickname = null, string? roomName = null)
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
        SenderNickname = senderNickname ?? string.Empty;
        RoomName = roomName ?? string.Empty;
        Cancel = new CancellationTokenSource();
    }

    public Guid Id { get; }

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

    /// <summary>0..1; 1 when <see cref="SizeBytes"/> is 0 so an empty file still completes.</summary>
    public double Fraction
        => SizeBytes == 0 ? (BytesDone > 0 ? 1 : 0) : Math.Clamp((double)BytesDone / SizeBytes, 0, 1);

    public bool IsComplete => BytesDone >= SizeBytes;

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

    public void CancelTransfer()
    {
        if (!Cancel.IsCancellationRequested)
        {
            Cancel.Cancel();
        }
    }

    public override string ToString() => $"{FileName} {Fraction:P0}";
}
