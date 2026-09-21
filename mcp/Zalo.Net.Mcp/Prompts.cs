// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Zalo.Net.Mcp;

/// <summary>
/// Preset MCP Prompt templates for AI conversation summaries and response drafting.
/// </summary>
[McpServerPromptType]
public sealed class Prompts
{
    [McpServerPrompt(Name = "summarize_chat_thread")]
    [Description("Generates a prompt template for the AI to summarize recent messages in a Zalo chat thread.")]
    public static GetPromptResult SummarizeChatThread(
        [Description("ID of the chat thread to summarize")] string threadId,
        [Description("Language for the summary (default 'en')")] string language = "en")
    {
        return new GetPromptResult
        {
            Description = $"Summarize Zalo chat thread {threadId}",
            Messages = [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"""
                            Use the `zalo_get_chat_history` tool with thread_id = "{threadId}" to retrieve the most recent messages.
                            Then concisely summarize key points, action items, requests, or scheduled events from this conversation in {language}.
                            """
                    }
                }
            ]
        };
    }

    [McpServerPrompt(Name = "draft_zalo_reply")]
    [Description("Generates a prompt template to help AI draft an appropriate reply to an incoming Zalo message.")]
    public static GetPromptResult DraftZaloReply(
        [Description("Content of the received message")] string receivedMessage,
        [Description("Reply style (e.g. 'Professional', 'Casual', 'Concise')")] string style = "Professional")
    {
        return new GetPromptResult
        {
            Description = "Draft Zalo reply",
            Messages = [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"""
                            I received the following Zalo message:
                            "{receivedMessage}"

                            Please suggest 3 reply options adhering to a '{style}' tone.
                            Each option should feel natural, match standard messaging etiquette, and include suitable emoji where appropriate.
                            """
                    }
                }
            ]
        };
    }
}
