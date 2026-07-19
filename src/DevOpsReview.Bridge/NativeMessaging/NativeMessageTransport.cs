using System.Buffers.Binary;
using System.Text.Json;
using DevOpsReview.Bridge.Protocol;

namespace DevOpsReview.Bridge.NativeMessaging;

public sealed class NativeMessageTransport(Stream input, Stream output)
{
    public const int MaxInboundMessageBytes = 256 * 1024;
    public const int MaxOutboundMessageBytes = 1024 * 1024;

    public async Task<BridgeRequestEnvelope?> ReadAsync(CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[sizeof(uint)];
        var lengthBytes = await ReadAtMostAsync(input, lengthBuffer, cancellationToken).ConfigureAwait(false);
        if (lengthBytes == 0)
        {
            return null;
        }

        if (lengthBytes != lengthBuffer.Length)
        {
            throw new NativeMessageProtocolException("Native message ended before its length prefix was complete.");
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(lengthBuffer);
        if (length is 0 or > MaxInboundMessageBytes)
        {
            throw new NativeMessageProtocolException(
                $"Native message length must be between 1 and {MaxInboundMessageBytes} bytes.");
        }

        var payload = new byte[length];
        var payloadBytes = await ReadAtMostAsync(input, payload, cancellationToken).ConfigureAwait(false);
        if (payloadBytes != payload.Length)
        {
            throw new NativeMessageProtocolException("Native message ended before its JSON payload was complete.");
        }

        try
        {
            return JsonSerializer.Deserialize<BridgeRequestEnvelope>(payload, BridgeJson.SerializerOptions)
                ?? throw new NativeMessageProtocolException("Native message JSON was empty.");
        }
        catch (JsonException exception)
        {
            throw new NativeMessageProtocolException("Native message JSON was invalid.", exception);
        }
    }

    public async Task WriteAsync(BridgeResponseEnvelope message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, BridgeJson.SerializerOptions);
        if (payload.Length > MaxOutboundMessageBytes)
        {
            throw new NativeMessageProtocolException(
                $"Native response exceeds the {MaxOutboundMessageBytes}-byte browser limit.");
        }

        var lengthBuffer = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(lengthBuffer, (uint)payload.Length);
        await output.WriteAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadAtMostAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[totalRead..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }
}

public sealed class NativeMessageProtocolException : Exception
{
    public NativeMessageProtocolException(string message)
        : base(message)
    {
    }

    public NativeMessageProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
