using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Lantern.Transport;

/// <summary>
/// Push side of the framing rules: bytes go in as they arrive, complete frames come out.
/// <para>
/// RFCOMM — like any serial stream — has no idea where one message ends and the next begins. A
/// 400 KB picture may arrive as four hundred 1 KB reads, two short chat lines may arrive glued
/// together in one read, and a read may stop half way through a header. This class is the only
/// place that has to cope with all of it, which is why it is separate from
/// <see cref="FrameCodec"/> (which only knows how to ask a stream for bytes).
/// </para>
/// Every frame looks like this on the wire, all lengths big-endian:
/// <code>
///   [4] payload length  = 4 + header length + body length
///   [4] header length
///   [n] UTF-8 JSON header      (a FrameHeader)
///   [m] binary body            (empty except for PICTURE and FILE_CHUNK)
/// </code>
/// </summary>
public sealed class FrameDecoder
{
    /// <summary>Bytes in front of every frame: the payload length.</summary>
    public const int LengthPrefixBytes = 4;

    /// <summary>Bytes in front of the JSON inside a frame: the header length.</summary>
    public const int HeaderLengthBytes = 4;

    /// <summary>
    /// Largest frame accepted. A picture is capped at 5 MB by the domain rules and file chunks are
    /// small, so anything bigger means the stream has desynchronised or is under attack — the
    /// length is attacker-controlled, so it must never be used to size a huge allocation.
    /// </summary>
    public const int MaxFrameBytes = 8 * 1024 * 1024;

    private const int GapBeforeCompacting = 64 * 1024;

    private byte[] buffer = new byte[8 * 1024];
    private int start;
    private int end;

    /// <summary>Bytes received but not yet turned into a frame.</summary>
    public int BufferedBytes => end - start;

    /// <summary>Adds bytes as they arrive from the transport.</summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        Reserve(data.Length);
        data.CopyTo(buffer.AsSpan(end));
        end += data.Length;
    }

    /// <summary>Adds the bytes of an array segment (same as <see cref="Append(ReadOnlySpan{byte})"/>).</summary>
    public void Append(byte[] data, int offset, int count) => Append(data.AsSpan(offset, count));

    /// <summary>
    /// Takes the next whole frame, or returns <see langword="false"/> when more bytes are needed.
    /// Never throws for "not enough data yet" — that is the normal case mid-stream.
    /// </summary>
    public bool TryNext(out Frame? frame)
    {
        frame = null;

        if (Available < LengthPrefixBytes)
        {
            return false;
        }

        var payloadLength = ReadLength(start);
        if (payloadLength > MaxFrameBytes)
        {
            throw new FrameException(
                $"Frame of {payloadLength:N0} bytes exceeds the {MaxFrameBytes:N0} byte limit; the stream is out of step.");
        }

        var frameLength = (long)LengthPrefixBytes + payloadLength;
        if (payloadLength < HeaderLengthBytes || Available < frameLength)
        {
            return false;
        }

        var headerLength = ReadLength(start + LengthPrefixBytes);
        if (headerLength > payloadLength - HeaderLengthBytes)
        {
            throw new FrameException("Frame header length runs past the end of the frame.");
        }

        var headerOffset = start + LengthPrefixBytes + HeaderLengthBytes;
        var json = Encoding.UTF8.GetString(buffer, headerOffset, (int)headerLength);

        var bodyLength = (int)payloadLength - HeaderLengthBytes - (int)headerLength;
        var body = new byte[bodyLength];
        if (bodyLength > 0)
        {
            Buffer.BlockCopy(buffer, headerOffset + (int)headerLength, body, 0, bodyLength);
        }

        FrameHeader header;
        try
        {
            header = FrameHeader.FromJson(json);
        }
        catch (JsonException ex)
        {
            throw new FrameException("Frame header was not valid JSON.", ex);
        }

        frame = new Frame(header, body);
        Consume((int)frameLength);
        return true;
    }

    /// <summary>Decodes every frame currently sitting in the buffer.</summary>
    public IEnumerable<Frame> ReadAllAvailable()
    {
        while (TryNext(out var frame))
        {
            yield return frame!;
        }
    }

    /// <summary>Drops everything buffered, e.g. when a connection is being reused.</summary>
    public void Reset() => start = end = 0;

    private int Available => end - start;

    private uint ReadLength(int offset) => BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset, LengthPrefixBytes));

    private void Reserve(int extra)
    {
        if (buffer.Length - Available >= extra)
        {
            return;
        }

        Compact();

        if (buffer.Length - Available < extra)
        {
            var size = buffer.Length;
            while (size - Available < extra)
            {
                size *= 2;
            }

            Array.Resize(ref buffer, size);
        }
    }

    private void Consume(int count)
    {
        start += count;

        if (start == end)
        {
            start = end = 0;
        }
        else if (start >= GapBeforeCompacting)
        {
            Compact();
        }
    }

    private void Compact()
    {
        if (start == 0)
        {
            return;
        }

        Buffer.BlockCopy(buffer, start, buffer, 0, Available);
        end -= start;
        start = 0;
    }
}
