param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$Text
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WinAct {
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
$h = $p.MainWindowHandle
if ($h -eq 0) { Write-Output "NO_WINDOW"; exit 1 }

[WinAct]::ShowWindow($h, 9) | Out-Null
$fg = [WinAct]::GetForegroundWindow()
$fgPid = 0
$fgThread = [WinAct]::GetWindowThreadProcessId($fg, [ref]$fgPid)
$myThread = [WinAct]::GetCurrentThreadId()
[WinAct]::AttachThreadInput($myThread, $fgThread, $true) | Out-Null
[WinAct]::BringWindowToTop($h) | Out-Null
[WinAct]::SetForegroundWindow($h) | Out-Null
[WinAct]::AttachThreadInput($myThread, $fgThread, $false) | Out-Null
Start-Sleep -Milliseconds 800

[System.Windows.Forms.SendKeys]::SendWait("^l")
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait($Text)
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
Write-Output "SENT=$Text"
