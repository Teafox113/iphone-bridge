$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

function Show-UninstallerMessage([string]$message, [string]$title, [System.Windows.Forms.MessageBoxIcon]$icon) {
    [System.Windows.Forms.MessageBox]::Show($message, $title, [System.Windows.Forms.MessageBoxButtons]::OK, $icon) | Out-Null
}

$expectedDirectory = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\iPhone Bridge'))
$installDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot)
if (-not [string]::Equals($installDirectory, $expectedDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
    Show-UninstallerMessage '安裝路徑驗證失敗，已停止移除。' 'iPhone Bridge 移除程式' ([System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}

$running = Get-Process -Name 'IPhoneBridge.Windows' -ErrorAction SilentlyContinue
if ($running) {
    Show-UninstallerMessage '請先關閉 iPhone Bridge，再重新執行移除。已收到的檔案不會刪除。' 'iPhone Bridge 移除程式' ([System.Windows.Forms.MessageBoxIcon]::Warning)
    exit 2
}

try {
    $programsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    $shortcutDirectory = Join-Path $programsDirectory 'iPhone Bridge'
    $shortcutPath = Join-Path $shortcutDirectory 'iPhone Bridge.lnk'
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }
    if ((Test-Path -LiteralPath $shortcutDirectory) -and -not (Get-ChildItem -LiteralPath $shortcutDirectory -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $shortcutDirectory -Force
    }

    Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\IPhoneBridge' -Recurse -Force -ErrorAction SilentlyContinue

    $quotedInstallDirectory = $installDirectory.Replace("'", "''")
    $removeCommand = "Start-Sleep -Seconds 2; Remove-Item -LiteralPath '$quotedInstallDirectory' -Recurse -Force"
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($removeCommand))
    Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-EncodedCommand', $encodedCommand) -WindowStyle Hidden
    Show-UninstallerMessage 'iPhone Bridge 已移除。你收到的檔案會保留在原本的儲存資料夾。' 'iPhone Bridge 移除程式' ([System.Windows.Forms.MessageBoxIcon]::Information)
    exit 0
}
catch {
    Show-UninstallerMessage ("移除失敗：" + $_.Exception.Message) 'iPhone Bridge 移除程式' ([System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}
