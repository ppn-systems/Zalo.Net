// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace Zalo.Net.Contracts;

/// <summary>
/// Source-generated System.Text.Json serialization context for Native AOT and trimming support.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ZaloSessionMaterial))]
[JsonSerializable(typeof(ZaloQrSession))]
[JsonSerializable(typeof(ZaloLoginState))]
[JsonSerializable(typeof(ZaloSession))]
[JsonSerializable(typeof(ZaloAttachment))]
[JsonSerializable(typeof(ZaloMessageEvent))]
[JsonSerializable(typeof(ZaloSessionStatusChanged))]
[JsonSerializable(typeof(ZaloSendResult))]
[JsonSerializable(typeof(ZaloUserProfile))]
[JsonSerializable(typeof(ZaloPrivacyOptions))]
[JsonSerializable(typeof(ZaloLinkMetadata))]
[JsonSerializable(typeof(ZaloStickerPack))]
[JsonSerializable(typeof(ZaloStickerItem))]
[JsonSerializable(typeof(ZaloOnlineStatus))]
[JsonSerializable(typeof(ZaloFriendshipStatus))]
[JsonSerializable(typeof(ZaloMutedConversation))]
[JsonSerializable(typeof(ZaloUnreadMark))]
[JsonSerializable(typeof(ZaloMention))]
[JsonSerializable(typeof(ZaloHistoryMessage))]
[JsonSerializable(typeof(ZaloGroupSettingsOptions))]
[JsonSerializable(typeof(ZaloGroupLinkDetail))]
[JsonSerializable(typeof(ZaloPollCreateOptions))]
[JsonSerializable(typeof(ZaloPollOption))]
[JsonSerializable(typeof(ZaloPollDetail))]
[JsonSerializable(typeof(ZaloBoardItem))]
[JsonSerializable(typeof(ZaloReminderRepeatMode))]
[JsonSerializable(typeof(ZaloReminderOptions))]
[JsonSerializable(typeof(ZaloReminderItem))]
[JsonSerializable(typeof(ZaloFriendRequestItem))]
[JsonSerializable(typeof(ZaloAliasEntry))]
[JsonSerializable(typeof(ZaloLabelItem))]
[JsonSerializable(typeof(ZaloQuickMessage))]
[JsonSerializable(typeof(ZaloProfileUpdateOptions))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloMention>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloStickerPack>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloStickerItem>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloMutedConversation>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloUnreadMark>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloHistoryMessage>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloPollOption>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloBoardItem>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloReminderItem>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloFriendRequestItem>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloAliasEntry>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloLabelItem>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<ZaloQuickMessage>))]
[JsonSerializable(typeof(System.Collections.Generic.IReadOnlyList<string>))]
[JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, string>))]
[JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, object?>))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(object))]
public partial class ZaloJsonContext : JsonSerializerContext
{
}
