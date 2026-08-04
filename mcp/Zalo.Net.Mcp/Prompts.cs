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
    [Description("Tạo prompt mẫu cho AI tóm tắt nội dung cuộc trò chuyện Zalo gần đây.")]
    public static GetPromptResult SummarizeChatThread(
        [Description("ID của cuộc trò chuyện cần tóm tắt")] string threadId,
        [Description("Ngôn ngữ tóm tắt (mặc định 'vi')")] string language = "vi")
    {
        return new GetPromptResult
        {
            Description = $"Tóm tắt nội dung cuộc trò chuyện Zalo {threadId}",
            Messages = [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"""
                            Hãy sử dụng tool `zalo_get_chat_history` với thread_id = "{threadId}" để đọc các tin nhắn gần nhất.
                            Sau đó tổng hợp ngắn gọn các ý chính, yêu cầu hoặc cuộc hẹn trong cuộc trò chuyện này bằng tiếng {language}.
                            """
                    }
                }
            ]
        };
    }

    [McpServerPrompt(Name = "draft_zalo_reply")]
    [Description("Tạo prompt mẫu giúp AI soạn thảo câu trả lời lịch sự cho tin nhắn Zalo vừa nhận.")]
    public static GetPromptResult DraftZaloReply(
        [Description("Nội dung tin nhắn nhận được")] string receivedMessage,
        [Description("Phong cách phản hồi (VD: 'Thân thiện', 'Lịch sự công việc', 'Ngắn gọn')")] string style = "Lịch sự công việc")
    {
        return new GetPromptResult
        {
            Description = "Soạn thảo phản hồi Zalo",
            Messages = [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"""
                            Tôi vừa nhận được tin nhắn Zalo sau:
                            "{receivedMessage}"

                            Hãy đề xuất 3 phương án trả lời bằng Tiếng Việt theo phong cách '{style}'.
                            Mỗi phương án phải tự nhiên, chuẩn văn phong nhắn tin Zalo và có kèm biểu tượng cảm xúc phù hợp.
                            """
                    }
                }
            ]
        };
    }
}
