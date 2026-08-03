using System;
using Xunit;
using ZaloApiException = Zalo.Net.Contracts.Exceptions.ZaloApiException;

namespace Zalo.Net.Contracts.Tests;

public class ZaloApiExceptionTests
{
    [Fact]
    public void Constructor_WithMessageAndCode_StoresPropertiesCorrectly()
    {
        string message = "API request failed";
        int code = 500;

        ZaloApiException ex = new(message, code);

        Assert.Equal(message, ex.Message);
        Assert.Equal(code, ex.Code);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Constructor_WithInnerException_StoresPropertiesCorrectly()
    {
        string message = "Network error";
        InvalidOperationException inner = new("Timeout");
        int code = 408;

        ZaloApiException ex = new(message, inner, code);

        Assert.Equal(message, ex.Message);
        Assert.Equal(code, ex.Code);
        Assert.Same(inner, ex.InnerException);
    }
}
