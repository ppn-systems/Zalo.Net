using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Zalo.Net.Cryptography.Tests;

public class WsFrameCodecTests
{
    [Fact]
    public void ParseHeader_4ByteHeader_ExtractsVersionCmdSubCmd()
    {
        byte[] header = [0x01, 0xF5, 0x01, 0x0A]; // Version=1, Cmd=501 (0x01F5 LE), SubCmd=10

        (byte version, int cmd, byte subCmd) = WsFrameCodec.ParseHeader(header);

        Assert.Equal(1, version);
        Assert.Equal(501, cmd);
        Assert.Equal(10, subCmd);
    }

    [Fact]
    public async Task DecodeFrameBodyAsync_RawJsonEnvelope_ReturnsParsedJson()
    {
        string json = "{\"encrypt\":0,\"data\":\"{\\\"msg\\\":\\\"hello\\\"}\"}";
        byte[] body = Encoding.UTF8.GetBytes(json);

        JsonNode? result = await WsFrameCodec.DecodeFrameBodyAsync(body, cipherKey: null);

        Assert.NotNull(result);
        Assert.Equal("hello", result?["msg"]?.GetValue<string>());
    }
}
