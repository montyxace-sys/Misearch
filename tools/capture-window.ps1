param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$Out
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WinCap {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
}
"@

$p = Get-Process -Id $ProcessId -ErrorAction Stop
for ($i = 0; $i -lt 40 -and $p.MainWindowHandle -eq 0; $i++) {
    Start-Sleep -Milliseconds 250
    $p.Refresh()
}
$h = $p.MainWindowHandle
if ($h -eq 0) { Write-Output "NO_WINDOW"; exit 1 }

[WinCap]::ShowWindow($h, 9) | Out-Null
$fg = [WinCap]::GetForegroundWindow()
$fgPid = 0
$fgThread = [WinCap]::GetWindowThreadProcessId($fg, [ref]$fgPid)
$myThread = [WinCap]::GetCurrentThreadId()
[WinCap]::AttachThreadInput($myThread, $fgThread, $true) | Out-Null
[WinCap]::BringWindowToTop($h) | Out-Null
[WinCap]::SetForegroundWindow($h) | Out-Null
[WinCap]::AttachThreadInput($myThread, $fgThread, $false) | Out-Null
Start-Sleep -Milliseconds 1200

$r = New-Object WinCap+RECT
[WinCap]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$hh = $r.Bottom - $r.Top
if ($w -le 0 -or $hh -le 0) { Write-Output "BAD_RECT"; exit 1 }

$bmp = New-Object System.Drawing.Bitmap($w, $hh)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $hh)))
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "RECT=${w}x${hh} AT=$($r.Left),$($r.Top) SAVED=$((Resolve-Path $Out).Path)"
