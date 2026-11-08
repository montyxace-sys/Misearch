param(
    [Parameter(Mandatory = $true)][string]$In,
    [Parameter(Mandatory = $true)][string]$OutIco,
    [string]$OutPng256
)

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$source = [System.Drawing.Image]::FromFile((Resolve-Path $In))

$frames = @()
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , @{ Size = $size; Data = $ms.ToArray() }
    $ms.Dispose()

    if ($size -eq 256 -and $OutPng256) {
        $bmp.Save($OutPng256, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bmp.Dispose()
}
$source.Dispose()

$fs = [System.IO.File]::Create($OutIco)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$frames.Count)

$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$f.Data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $f.Data.Length
}
foreach ($f in $frames) { $bw.Write($f.Data) }

$bw.Close()
$fs.Close()
Write-Output "ICO=$((Resolve-Path $OutIco).Path) SIZE=$((Get-Item $OutIco).Length)"
