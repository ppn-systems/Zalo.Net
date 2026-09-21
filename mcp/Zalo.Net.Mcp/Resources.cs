// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Zalo.Net.Mcp;

/// <summary>
/// Runtime guide resources and dynamic data endpoints for AI assistants.
/// </summary>
[McpServerResourceType]
public sealed class Resources
{
    [McpServerResource(UriTemplate = "guide://overview", Name = "overview", MimeType = "text/markdown")]
    [Description("Overview and operational guidelines for Zalo MCP Server.")]
    public static string Overview() =>
        """
        # Zalo.Net MCP Server — User Guide

        Zalo.Net MCP Server bridges AI assistants (Claude, Antigravity, ChatGPT, Cursor) with your Zalo Web accounts.

        ## Key Capabilities:
        1. **Authentication (`zalo_login_qr`, `zalo_check_qr_status`, `zalo_list_accounts`, `zalo_switch_account`)**: Multi-account QR code login and active account switching.
        2. **Messaging (`zalo_send_text`, `zalo_quote_message`, `zalo_send_file`, `zalo_send_sticker`, `zalo_react_message`, `zalo_recall_message`)**: Direct and group chat interactions.
        3. **Fast Intelligence & SQLite FTS5 (`zalo_smart_search`, `zalo_get_extracted_entities`, `zalo_get_chat_history`)**: Sub-millisecond full-text search, auto-extracted bank accounts, phone numbers, and links.
        4. **Contacts & Groups (`zalo_find_user`, `zalo_list_contacts`, `zalo_list_groups`, `zalo_create_group`, `zalo_manage_group`)**: Lookup users by phone, create groups, and manage members.

        ## Recommended Workflow:
        - **Step 1**: Call `zalo_get_profile` to verify whether an account is authenticated.
        - **Step 2**: If unauthenticated, call `zalo_login_qr` to return a QR code for user mobile scanning.
        - **Step 3**: Once connected, use `zalo_get_chat_history` or `zalo_smart_search` to inspect conversation context before sending messages via `zalo_send_text`.
        """;

    [McpServerResource(UriTemplate = "guide://smart-agent", Name = "smart-agent", MimeType = "text/markdown")]
    [Description("Best practices and operational rules for AI agents interacting with Zalo MCP.")]
    public static string SmartAgentGuide() =>
        """
        # Smart AI Agent Guidelines for Zalo MCP

        1. **Querying Bank Accounts / Phone Numbers**:
           When asked for bank details or contact numbers, call `zalo_get_extracted_entities` with `entity_type = 'bank_card'` or `'phone'` instead of querying raw chat history.

        2. **Safe Messaging**:
           - Verify recipient `thread_id` or User ID using `zalo_find_user` or `zalo_list_contacts` before sending messages.
           - For group chats, ensure `threadType = 'Group'` and `thread_id` is a valid Group ID.

        3. **Summaries & Daily Reports**:
           Use `zalo_get_chat_summary` for an executive overview of recent conversations, followed by `zalo_get_chat_history` to drill into specific discussions.
        """;
}
