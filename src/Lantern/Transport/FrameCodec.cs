using System.Buffers.Binary;

namespace Lantern.Transport;

/// <summary>
/// Turns frames into bytes and bytes back into frames (T2.1).
/// <para>
/// Bluetooth RFCOMM hands over one continuous byte stream: nothing marks the end of a message, so a
/// picture could otherwise be sliced in half and the second half parsed as chat text. Every frame is
/// therefore length-prefixed (the layout is documented on <see cref="FrameDecoder"/>) and the JSON
/// header stays small because bulk bytes ride in the body, never base64-encoded into the header.
/// </para>
/// <see cref="ReadAsync(Stream, CancellationToken)"/> reads exactly one frame and stops at its last
/// byte, so it is safe to call in a loop without keeping any state between calls.
/// </summary>
public static class FrameCodec
{
    /// <summary>
    /// Bytes per FILE_CHUNK body. Small on purpose: an RFCOMM packet is a few hundred bytes and the
    /// server interleaves chat traffic between chunks, so one big file must not starve a room.
    /// </summary>
    public const int ChunkBytes = 8 * 1024;

    /// <summary>Serialises one frame into a self-contained byte array.</summary>
    public static byte[] Encode(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var json = System.Text.Encoding.UTF8.GetBytes(frame.JsonHeader);
        var payloadLength = FrameDecoder.HeaderLengthBytes + json.Length + frame.Body.Length;
        if (payloadLength > FrameDecoder.MaxFrameBytes)
        {
            throw new FrameException(
                $"Frame of {payloadLength:N0} bytes exceeds the {FrameDecoder.MaxFrameBytes:N0} byte limit.");
        }

        var bytes = new byte[FrameDecoder.LengthPrefixBytes + payloadLength];
        var written = 0;

        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(written), (uint)payloadLength);
        written += FrameDecoder.LengthPrefixBytes;

        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(written), (uint)json.Length);
        written += FrameDecoder.HeaderLengthBytes;

        json.CopyTo(bytes.AsSpan(written));
        written += json.Length;

        frame.Body.CopyTo(bytes.AsSpan(written));

        return bytes;
    }

    /// <summary>Writes one frame. The stream is not flushed: a link flushes per frame it owns.</summary>
    public static async Task WriteAsync(Stream stream, Frame frame, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var bytes = Encode(frame);
        await stream.WriteAsync(bytes, cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one frame, or null when the stream ended cleanly between frames — which is how a
    /// closed connection is reported to the read loop rather than an exception.
    /// </summary>
    public static async Task<Frame?> ReadAsync(Stream stream, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var prefix = await ReadExactlyAsync(stream, FrameDecoder.LengthPrefixBytes, cancel, allowCleanEof: true)
            .ConfigureAwait(false);

        if (prefix is null)
        {
            return null;
        }

        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(prefix);
        if (payloadLength < FrameDecoder.HeaderLengthBytes || payloadLength > FrameDecoder.MaxFrameBytes)
        {
            throw new FrameException(
                $"Frame of {payloadLength:N0} bytes is not a legal length (limit {FrameDecoder.MaxFrameBytes:N0}).");
        }

        var headerLengthBytes = await ReadExactlyAsync(stream, FrameDecoder.HeaderLengthBytes, cancel, allowCleanEof: false)
            .ConfigureAwait(false);

        var headerLength = BinaryPrimitives.ReadUInt32BigEndian(headerLengthBytes!);
        if (headerLength > payloadLength - FrameDecoder.HeaderLengthBytes)
        {
            throw new FrameException("Frame header length runs past the end of the frame.");
        }

        var rest = await ReadExactlyAsync(
            stream, (int)(payloadLength - FrameDecoder.HeaderLengthBytes), cancel, allowCleanEof: false)
            .ConfigureAwait(false);

        var jsonLength = (int)headerLength;
        var json = System.Text.Encoding.UTF8.GetString(rest!, 0, jsonLength);
        var body = new byte[rest!.Length - jsonLength];
        Buffer.BlockCopy(rest, jsonLength, body, 0, body.Length);

        FrameHeader header;
        try
        {
            header = FrameHeader.FromJson(json);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new FrameException("Frame header was not valid JSON.", ex);
        }

        return new Frame(header, body);
    }

    /// <summary>
    /// Reads <paramref name="count"/> bytes, looping until they have all arrived: a stream — least of
    /// all an RFCOMM one — is allowed to return fewer bytes than asked for.
    /// </summary>
    /// <returns>
    /// The buffer; null only when <paramref name="allowCleanEof"/> is set and the stream had nothing
    /// left at all.
    /// </returns>
    internal static async Task<byte[]?> ReadExactlyAsync(
        Stream stream, int count, CancellationToken cancel, bool allowCleanEof)
    {
        var buffer = new byte[count];
        var read = 0;

        while (read < count)
        {
            var got = await stream.ReadAsync(buffer.AsMemory(read, count - read), cancel).ConfigureAwait(false);
            if (got == 0)
            {
                if (read == 0 && allowCleanEof)
                {
                    return null;
                }

                throw new FrameException($"Connection closed with {count - read} of {count} frame bytes missing.");
            }

            read += got;
        }

        return buffer;
    }

    /// <summary>
    /// Splits a file into the FILE_CHUNK bodies to send, in order, each at most
    /// <see cref="ChunkBytes"/> long. Streaming: the whole file is never held in memory.
    /// </summary>
    public static async IAsyncEnumerable<(int Sequence, byte[] Bytes)> ChunkAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancel = default)
    {
        var buffer = new byte[ChunkBytes];
        var sequence = 0;

        await using var file = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            BufferSize = ChunkBytes,
            // Reads of a file the sender is still writing must not block the connection.
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });

        while (true)
        {
            var got = await ReadAtLeastOneAsync(file, buffer, cancel).ConfigureAwait(false);
            if (got == 0)
            {
                yield break;
            }

            var slice = new byte[got];
            Buffer.BlockCopy(buffer, 0, slice, 0, got);
            yield return (sequence++, slice);
        }
    }

    /// <summary>
    /// Stream.Read may return fewer bytes than the buffer holds, and for a file that means a chunk
    /// smaller than the buffer is legitimate — but zero before the end of file is not.
    /// </summary>
    private static async Task<int> ReadAtLeastOneAsync(Stream file, byte[] buffer, CancellationToken cancel)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var got = await file.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancel).ConfigureAwait(false);
            if (got == 0)
            {
                return read;
            }

            read += got;
        }

        return read;
    }
}
