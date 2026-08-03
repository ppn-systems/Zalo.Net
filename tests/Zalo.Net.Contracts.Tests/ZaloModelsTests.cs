using System;
using Xunit;

namespace Zalo.Net.Contracts.Tests;

public class ZaloModelsTests
{
    [Fact]
    public void ZaloSessionMaterial_RecordProperties_EqualAndImmutable()
    {
        ZaloSessionMaterial material1 = new("{}", "secret", "imei123", "uid456", "Agent/1.0");
        ZaloSessionMaterial material2 = new("{}", "secret", "imei123", "uid456", "Agent/1.0");

        Assert.Equal(material1, material2);
        Assert.Equal("vi", material1.Language);
    }

    [Fact]
    public void ZaloQrSession_RecordProperties_VerifyFields()
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(5);
        ZaloQrSession session = new(id, "base64img", "code123", expires);

        Assert.Equal(id, session.SessionId);
        Assert.Equal("base64img", session.QrImageBase64);
        Assert.Equal("code123", session.QrCode);
        Assert.Equal(expires, session.ExpiresAt);
    }
}
