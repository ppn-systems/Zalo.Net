// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Zalo.Net.Contracts;

namespace Zalo.Net.Builders;

/// <summary>
/// Represents a built outbound message ready for dispatching.
/// </summary>
public sealed record ZaloOutboundMessage(
    string? Text = null,
    string? QuoteMsgId = null,
    string? QuoteOwnerUid = null,
    ZaloBankCard? BankCard = null,
    ZaloContactCard? ContactCard = null,
    TimeSpan? Ttl = null,
    IReadOnlyList<ZaloMention>? Mentions = null
);

/// <summary>
/// Fluent builder for composing rich Zalo outbound messages.
/// </summary>
public sealed class ZaloMessageBuilder
{
    private string? _text;
    private string? _quoteMsgId;
    private string? _quoteOwnerUid;
    private ZaloBankCard? _bankCard;
    private ZaloContactCard? _contactCard;
    private TimeSpan? _ttl;
    private List<ZaloMention>? _mentions;

    /// <summary>
    /// Creates a new instance of <see cref="ZaloMessageBuilder"/>.
    /// </summary>
    public static ZaloMessageBuilder Create() => new();

    /// <summary>
    /// Sets the text content of the message.
    /// </summary>
    public ZaloMessageBuilder WithText(string text)
    {
        _text = text;
        return this;
    }

    /// <summary>
    /// Configures a reply / quote message reference.
    /// </summary>
    public ZaloMessageBuilder WithQuote(string msgId, string ownerUid)
    {
        _quoteMsgId = msgId;
        _quoteOwnerUid = ownerUid;
        return this;
    }

    /// <summary>
    /// Attaches a bank account card for quick transfer.
    /// </summary>
    public ZaloMessageBuilder WithBankCard(string binBank, string accountNumber, string accountName)
    {
        _bankCard = new ZaloBankCard(binBank, accountNumber, accountName);
        return this;
    }

    /// <summary>
    /// Attaches a bank account card for quick transfer.
    /// </summary>
    public ZaloMessageBuilder WithBankCard(ZaloBankCard bankCard)
    {
        _bankCard = bankCard;
        return this;
    }

    /// <summary>
    /// Attaches a contact card recommendation.
    /// </summary>
    public ZaloMessageBuilder WithContactCard(string userId, string? phoneNumber = null, string? qrCodeUrl = null)
    {
        _contactCard = new ZaloContactCard(userId, phoneNumber, qrCodeUrl);
        return this;
    }

    /// <summary>
    /// Attaches a contact card recommendation.
    /// </summary>
    public ZaloMessageBuilder WithContactCard(ZaloContactCard contactCard)
    {
        _contactCard = contactCard;
        return this;
    }

    /// <summary>
    /// Configures an auto-disappearing / ephemeral message TTL duration.
    /// </summary>
    public ZaloMessageBuilder WithTtl(TimeSpan ttl)
    {
        _ttl = ttl;
        return this;
    }

    /// <summary>
    /// Adds a user @mention at the specified character position and length.
    /// </summary>
    public ZaloMessageBuilder WithMention(string uid, int position, int length)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        _mentions ??= [];
        bool isAll = string.Equals(uid, ZaloConstants.Protocol.MentionAllUid, StringComparison.Ordinal);
        _mentions.Add(new ZaloMention(uid, position, length, IsMentionAll: isAll));
        return this;
    }

    /// <summary>
    /// Adds an @All group mention at the specified character position and length (defaults to pos 0, len 4 for "@All").
    /// </summary>
    public ZaloMessageBuilder WithMentionAll(int position = 0, int length = 4)
    {
        _mentions ??= [];
        _mentions.Add(new ZaloMention(ZaloConstants.Protocol.MentionAllUid, position, length, IsMentionAll: true));
        return this;
    }

    /// <summary>
    /// Builds the final <see cref="ZaloOutboundMessage"/>.
    /// </summary>
    public ZaloOutboundMessage Build()
    {
        return new ZaloOutboundMessage(
            Text: _text,
            QuoteMsgId: _quoteMsgId,
            QuoteOwnerUid: _quoteOwnerUid,
            BankCard: _bankCard,
            ContactCard: _contactCard,
            Ttl: _ttl,
            Mentions: _mentions
        );
    }
}
