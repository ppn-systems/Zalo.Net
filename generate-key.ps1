# Script tạo file Zalo.Net.snk chuẩn CAPI cho Strong-Name Signing và xuất ra mã Base64 cho GitHub Secrets
$snkPath = "src/Zalo.Net.snk"

Write-Host "Creating Strong Name Key (.snk) with CAPI RSAPrivateKey blob..." -ForegroundColor Cyan
$csp = New-Object System.Security.Cryptography.RSACryptoServiceProvider(2048)
$keyBytes = $csp.ExportCspBlob($true)

[System.IO.File]::WriteAllBytes($snkPath, $keyBytes)
$base64 = [System.Convert]::ToBase64String($keyBytes)

Write-Host "✅ Created file: $snkPath" -ForegroundColor Green
Write-Host ""
Write-Host "==========================================================================" -ForegroundColor DarkGray
Write-Host " BASE64 STRING FOR GITHUB SECRET (SIGNING_KEY):" -ForegroundColor Yellow
Write-Host "==========================================================================" -ForegroundColor DarkGray
Write-Host $base64
Write-Host "==========================================================================" -ForegroundColor DarkGray
