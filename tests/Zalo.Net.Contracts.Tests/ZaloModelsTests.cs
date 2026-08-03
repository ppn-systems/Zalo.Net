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

    [Fact]
    public void ZaloUserProfile_RecordProperties_VerifyFields()
    {
        ZaloUserProfile profile = new("uid123", "Nguyen Van A", "https://avatar.com/a.jpg");

        Assert.Equal("uid123", profile.Uid);
        Assert.Equal("Nguyen Van A", profile.DisplayName);
        Assert.Equal("https://avatar.com/a.jpg", profile.AvatarUrl);
    }

    [Fact]
    public void ZaloSendResult_RecordProperties_VerifyFields()
    {
        ZaloSendResult result = new("msg_999");
        Assert.Equal("msg_999", result.MsgId);
    }

    [Fact]
    public void ZaloSessionStatusChanged_RecordProperties_VerifyFields()
    {
        ZaloSessionStatusChanged evt = new("uid123", ZaloConnectionStatus.Reconnecting, "Connection lost");

        Assert.Equal("uid123", evt.Uid);
        Assert.Equal(ZaloConnectionStatus.Reconnecting, evt.Status);
        Assert.Equal("Connection lost", evt.Reason);
    }

    [Fact]
    public void ZaloAttachment_RecordProperties_VerifyFields()
    {
        ZaloAttachment attachment = new("https://zalo.me/file.pdf", "file.pdf", "share.file");

        Assert.Equal("https://zalo.me/file.pdf", attachment.Url);
        Assert.Equal("file.pdf", attachment.FileName);
        Assert.Equal("share.file", attachment.Type);
    }
}
