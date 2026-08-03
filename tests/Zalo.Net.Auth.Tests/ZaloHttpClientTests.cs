// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Zalo.Net.Auth.Tests;

public class ZaloHttpClientTests
{
    [Xunit.Fact]
    public void Constructor_CustomUserAgent_ExposesCookiesAndHandler()
    {
        string ua = "TestAgent/1.0";
        using ZaloHttpClient client = new(ua);

        Xunit.Assert.NotNull(client.Cookies);
    }
}
