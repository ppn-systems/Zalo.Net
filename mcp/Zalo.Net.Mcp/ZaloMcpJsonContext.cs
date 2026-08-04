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
public partial class ZaloMcpJsonContext : JsonSerializerContext;
