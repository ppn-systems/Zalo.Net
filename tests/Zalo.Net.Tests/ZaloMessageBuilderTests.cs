// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Xunit;
using Zalo.Net.Builders;

namespace Zalo.Net.Tests;

public class ZaloMessageBuilderTests
{
    [Fact]
    public void Build_WithAllFields_PopulatesMessageCorrectly()
    {
        // Arrange & Act
        ZaloOutboundMessage msg = ZaloMessageBuilder.Create()
            .WithText("@All Hello @Bob")
            .WithQuote("msg_123", "owner_456")
            .WithBankCard("970458", "123456789", "NGUYEN VAN A")
            .WithContactCard("user_789", "0912345678")
            .WithTtl(TimeSpan.FromMinutes(5))
            .WithMentionAll(0, 4)
            .WithMention("bob_uid", 11, 4)
            .Build();

        // Assert
        Assert.Equal("@All Hello @Bob", msg.Text);
        Assert.Equal("msg_123", msg.QuoteMsgId);
        Assert.Equal("owner_456", msg.QuoteOwnerUid);
        Assert.NotNull(msg.BankCard);
        Assert.Equal("970458", msg.BankCard.BinBank);
        Assert.Equal("123456789", msg.BankCard.AccountNumber);
        Assert.Equal("NGUYEN VAN A", msg.BankCard.AccountName);
        Assert.NotNull(msg.ContactCard);
        Assert.Equal("user_789", msg.ContactCard.UserId);
        Assert.Equal("0912345678", msg.ContactCard.PhoneNumber);
        Assert.Equal(TimeSpan.FromMinutes(5), msg.Ttl);
        Assert.NotNull(msg.Mentions);
        Assert.Equal(2, msg.Mentions.Count);
        Assert.True(msg.Mentions[0].IsMentionAll);
        Assert.Equal("-1", msg.Mentions[0].Uid);
        Assert.Equal("bob_uid", msg.Mentions[1].Uid);

        string? mentionInfo = Endpoints.MessageApis.BuildMentionInfoJson(msg.Text!, msg.Mentions);
        Assert.NotNull(mentionInfo);
        Assert.Contains("\"uid\":\"-1\"", mentionInfo, StringComparison.Ordinal);
        Assert.Contains("\"type\":1", mentionInfo, StringComparison.Ordinal);
        Assert.Contains("\"uid\":\"bob_uid\"", mentionInfo, StringComparison.Ordinal);

        string? autoMentionAll = Endpoints.MessageApis.BuildMentionInfoJson("Chào @All mọi người nhé");
        Assert.NotNull(autoMentionAll);
        Assert.Contains("\"pos\":5", autoMentionAll, StringComparison.Ordinal);
        Assert.Contains("\"uid\":\"-1\"", autoMentionAll, StringComparison.Ordinal);
    }
}
