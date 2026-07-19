using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using DevOpsReview.Bridge.NativeMessaging;
using DevOpsReview.Bridge.Protocol;

namespace DevOpsReview.Bridge.Tests.NativeMessaging;

public sealed class NativeMessageTransportTests
{
    [Fact]
    public async Task ReadAsyncUsesUtf8ByteLength()
    {
        const string json = """
            {"type":"host.status","requestId":"状态-1","payload":{}}
            """;
        var input = Frame(json);
        await using var output = new MemoryStream();
        var transport = new NativeMessageTransport(input, output);

        var message = await transport.ReadAsync(CancellationToken.None);

        Assert.NotNull(message);
        Assert.Equal("状态-1", message.RequestId);
    }

    [Fact]
    public async Task ReadAsyncRejectsOversizedInputBeforeAllocation()
    {
        var prefix = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(
            prefix,
            NativeMessageTransport.MaxInboundMessageBytes + 1u);
        await using var input = new MemoryStream(prefix);
        await using var output = new MemoryStream();
        var transport = new NativeMessageTransport(input, output);

        await Assert.ThrowsAsync<NativeMessageProtocolException>(() =>
            transport.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WriteAsyncFramesJsonForBrowser()
    {
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();
        var transport = new NativeMessageTransport(input, output);

        await transport.WriteAsync(
            new BridgeResponseEnvelope(
                BridgeMessageTypes.ReviewDelta,
                "request-1",
                new { delta = "正在分析" }),
            CancellationToken.None);

        var bytes = output.ToArray();
        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, sizeof(uint)));
        Assert.Equal((uint)(bytes.Length - sizeof(uint)), payloadLength);

        using var document = JsonDocument.Parse(bytes.AsMemory(sizeof(uint)));
        Assert.Equal("正在分析", document.RootElement.GetProperty("payload").GetProperty("delta").GetString());
    }

    private static MemoryStream Frame(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var framed = new byte[sizeof(uint) + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(framed, (uint)payload.Length);
        payload.CopyTo(framed.AsSpan(sizeof(uint)));
        return new MemoryStream(framed);
    }
}
