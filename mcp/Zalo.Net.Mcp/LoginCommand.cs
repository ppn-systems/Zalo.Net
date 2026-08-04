// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using Zalo.Net.Contracts;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp;

/// <summary>
/// CLI Interactive Login command (<c>Zalo.Net.Mcp --login</c>) allowing users to pre-authenticate
/// via Terminal QR Code scanning and save session to SQLite.
/// </summary>
public static class LoginCommand
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== Zalo.Net.Mcp Interactive QR Login ===");

        ZaloDatabase db = new();
        db.Initialize();
        MessageRepository repo = new(db);
        using ZaloSessionManager sessionManager = new(repo);

        Console.WriteLine("[INFO] Generating Zalo QR Code...");
        ZaloQrSession qrSession = await sessionManager.StartQrLoginAsync().ConfigureAwait(false);

        string tempQrFile = Path.Combine(Path.GetTempPath(), "zalo_qr.png");
        byte[] imageBytes = Convert.FromBase64String(qrSession.QrImageBase64);
        await File.WriteAllBytesAsync(tempQrFile, imageBytes).ConfigureAwait(false);

        Console.WriteLine($"[ACTION REQUIRED] QR Code saved to: {tempQrFile}");
        Console.WriteLine("[ACTION REQUIRED] Mở ứng dụng Zalo trên điện thoại và quét mã QR.");

        // Automatically open QR image in default OS image viewer
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _ = Process.Start(new ProcessStartInfo(tempQrFile) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                _ = Process.Start("open", tempQrFile);
            }
            else if (OperatingSystem.IsLinux())
            {
                _ = Process.Start("xdg-open", tempQrFile);
            }
        }
        catch
        {
            // Ignore if image viewer cannot open automatically
        }

        Console.WriteLine("\nWaiting for QR scan & mobile confirmation (Đang chờ quét và xác nhận trên điện thoại)...");
        Stopwatch sw = Stopwatch.StartNew();

        while (sw.Elapsed < TimeSpan.FromSeconds(120))
        {
            ZaloLoginState state = await sessionManager.PollQrStatusAsync(qrSession.SessionId).ConfigureAwait(false);
            if (state.Status == ZaloLoginStatus.Scanned)
            {
                Console.WriteLine($"[INFO] Mã QR đã được quét bởi '{state.DisplayName}'. Vui lòng bấm 'Đăng nhập' trên điện thoại...");
            }
            else if (state.Status == ZaloLoginStatus.Connected)
            {
                Console.WriteLine($"\n[SUCCESS] Đăng nhập Zalo thành công cho tài khoản: {state.DisplayName} (UID: {state.SessionId})!");
                Console.WriteLine("[INFO] Phiên đăng nhập đã được lưu an toàn vào Local SQLite Database.");
                Console.WriteLine("Bây giờ bạn có thể đóng cửa sổ này và sử dụng ChatGPT / Claude / Antigravity.");

                // Cleanup temp file
                if (File.Exists(tempQrFile))
                {
                    try
                    {
                        File.Delete(tempQrFile);
                    }
                    catch
                    {
                        // Ignore cleanup error
                    }
                }

                return;
            }
            else if (state.Status == ZaloLoginStatus.Expired || state.Status == ZaloLoginStatus.Declined)
            {
                Console.WriteLine($"\n[ERROR] Đăng nhập thất bại: {state.Status}");
                return;
            }

            await Task.Delay(2000).ConfigureAwait(false);
        }

        Console.WriteLine("\n[EXPIRED] Đã hết thời gian chờ quét mã QR (Timeout 120s). Vui lòng thử lại lệnh --login.");
    }
}
