// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Zalo.Net.Contracts;

namespace Zalo.Net.Auth.Tests;

public class ZaloHttpClientTests
{
    [Fact]
    public void Constructor_CustomUserAgent_ExposesCookiesAndHandler()
    {
        string ua = "TestAgent/1.0";
        using ZaloHttpClient client = new(ua);

        Assert.NotNull(client.Cookies);
    }

    [Fact]
    public async Task RequestAsync_WhenPrivacyGuardBlocksTypingSeenDelivered_ReturnsSyntheticSuccessWithoutNetworkCall()
    {
        using ZaloHttpClient client = new("TestAgent/1.0")
        {
            PrivacyOptions = new ZaloPrivacyOptions(BlockTyping: true, BlockDelivered: true, BlockSeen: true)
        };

        Assert.True(client.IsBlockedByPrivacyGuard("https://tt-chat3-wpa.chat.zalo.me/api/message/typing?zpw_ver=694"));
        Assert.True(client.IsBlockedByPrivacyGuard("https://tt-group-wpa.chat.zalo.me/api/group/seenv2?zpw_ver=694"));
        Assert.True(client.IsBlockedByPrivacyGuard("https://tt-chat3-wpa.chat.zalo.me/api/message/deliveredv2?zpw_ver=694"));
        Assert.False(client.IsBlockedByPrivacyGuard("https://tt-chat3-wpa.chat.zalo.me/api/message/sms?zpw_ver=694"));

        using HttpResponseMessage resp = await client.RequestAsync(
            "https://tt-chat3-wpa.chat.zalo.me/api/message/typing?zpw_ver=694",
            HttpMethod.Post);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"error_code\":0", body, System.StringComparison.Ordinal);
    }
}
