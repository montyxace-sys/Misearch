param(
    [Parameter(Mandatory=$true)][string]$Exe,
    [Parameter(Mandatory=$true)][string]$Out
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Cap {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@

$proc = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 3

for ($i = 0; $i -lt 20; $i++) {
    $proc.Refresh()
    if ($proc.HasExited) { "EXITED code=" + $proc.ExitCode; exit 1 }
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 500
}

$proc.Refresh()
$hwnd = $proc.MainWindowHandle
"PID=" + $proc.Id + " HWND=" + $hwnd
if ($hwnd -eq [IntPtr]::Zero) { "NO_WINDOW"; exit 2 }

[Win32Cap]::ShowWindow($hwnd, 9) | Out-Null
[Win32Cap]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 1500

$rect = New-Object Win32Cap+RECT
[Win32Cap]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
"RECT=" + $width + "x" + $height

$bmp = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

"SAVED=" + $Out
