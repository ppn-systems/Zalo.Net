// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json.Serialization;
using Zalo.Net.Contracts;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp;

/// <summary>
/// Source generator JsonSerializerContext for Zalo MCP DTOs for zero-allocation high-performance serialization.
/// </summary>
[JsonSerializable(typeof(SavedMessage))]
[JsonSerializable(typeof(IReadOnlyList<SavedMessage>))]
[JsonSerializable(typeof(SavedThread))]
[JsonSerializable(typeof(IReadOnlyList<SavedThread>))]
[JsonSerializable(typeof(ExtractedEntity))]
[JsonSerializable(typeof(IReadOnlyList<ExtractedEntity>))]
[JsonSerializable(typeof(SmartSearchResult))]
[JsonSerializable(typeof(ExtractedReminder))]
[JsonSerializable(typeof(IReadOnlyList<ExtractedReminder>))]
[JsonSerializable(typeof(ContactInsight))]
[JsonSerializable(typeof(AutoReplyRule))]
[JsonSerializable(typeof(IReadOnlyList<AutoReplyRule>))]
[JsonSerializable(typeof(ZaloUserProfile))]
[JsonSerializable(typeof(ZaloFriendInfo))]
[JsonSerializable(typeof(IReadOnlyList<ZaloFriendInfo>))]
[JsonSerializable(typeof(ZaloGroupInfo))]
[JsonSerializable(typeof(IReadOnlyList<ZaloGroupInfo>))]
[JsonSerializable(typeof(ZaloGroupCreateResult))]
[JsonSerializable(typeof(ZaloSessionMaterial))]
[JsonSerializable(typeof(ZaloAccountSummary))]
[JsonSerializable(typeof(IReadOnlyList<ZaloAccountSummary>))]
[JsonSerializable(typeof(ScheduledMessageRow))]
[JsonSerializable(typeof(IReadOnlyList<ScheduledMessageRow>))]
[JsonSerializable(typeof(ZaloStickerItem))]
[JsonSerializable(typeof(IReadOnlyList<ZaloStickerItem>))]
[JsonSerializable(typeof(ZaloStickerPack))]
[JsonSerializable(typeof(IReadOnlyList<ZaloStickerPack>))]
[JsonSerializable(typeof(ZaloLinkMetadata))]
[JsonSerializable(typeof(ZaloOnlineStatus))]
[JsonSerializable(typeof(ZaloFriendshipStatus))]
[JsonSerializable(typeof(ZaloHistoryMessage))]
[JsonSerializable(typeof(IReadOnlyList<ZaloHistoryMessage>))]
[JsonSerializable(typeof(ZaloGroupLinkDetail))]
[JsonSerializable(typeof(ZaloPollOption))]
[JsonSerializable(typeof(IReadOnlyList<ZaloPollOption>))]
[JsonSerializable(typeof(ZaloPollDetail))]
[JsonSerializable(typeof(ZaloBoardItem))]
[JsonSerializable(typeof(IReadOnlyList<ZaloBoardItem>))]
[JsonSerializable(typeof(ZaloReminderItem))]
[JsonSerializable(typeof(IReadOnlyList<ZaloReminderItem>))]
[JsonSerializable(typeof(ZaloFriendRequestItem))]
[JsonSerializable(typeof(IReadOnlyList<ZaloFriendRequestItem>))]
[JsonSerializable(typeof(ZaloAliasEntry))]
[JsonSerializable(typeof(IReadOnlyList<ZaloAliasEntry>))]
[JsonSerializable(typeof(ZaloLabelItem))]
[JsonSerializable(typeof(IReadOnlyList<ZaloLabelItem>))]
[JsonSerializable(typeof(ZaloQuickMessage))]
[JsonSerializable(typeof(IReadOnlyList<ZaloQuickMessage>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
public partial class ZaloMcpJsonContext : JsonSerializerContext;
