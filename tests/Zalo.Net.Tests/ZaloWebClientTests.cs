using System;
using Xunit;
using Zalo.Net.Contracts;

namespace Zalo.Net.Tests;

public class ZaloWebClientTests
{
    [Fact]
    public void Dispose_CleanState_DisposesWithoutThrowing()
    {
        using ZaloWebClient client = new();
        Assert.NotNull(client);
    }

    [Fact]
    public void ConsumePendingMaterial_UnknownSession_ReturnsNull()
    {
        using ZaloWebClient client = new();
        ZaloSessionMaterial? material = client.ConsumePendingMaterial(Guid.NewGuid());

        Assert.Null(material);
    }
}
