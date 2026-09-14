Add-Type -AssemblyName System.Drawing

$src = "C:\Users\felip\WhisperLowCost\source\voicepod-logo.jpg"
$dest = "C:\Users\felip\WhisperLowCost\VoiceFlow.App\Resources\voiceflow.ico"

$bmp = [System.Drawing.Bitmap]::FromFile($src)
$sizes = @(16, 32, 48, 64, 128, 256)
$streams = @()

foreach ($s in $sizes) {
    $t = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($t)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($bmp, 0, 0, $s, $s)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $t.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $t.Dispose()
    $bytes = $ms.ToArray()
    $ms.Dispose()

    $streams += [PSCustomObject]@{
        Size = $s
        Bytes = $bytes
    }
}
$bmp.Dispose()

$fs = [System.IO.File]::Create($dest)
$bw = New-Object System.IO.BinaryWriter $fs

# Header: 2 bytes reserved (0), 2 bytes type (1 = icon), 2 bytes count
$bw.Write([System.Int16]0)
$bw.Write([System.Int16]1)
$bw.Write([System.Int16]$streams.Count)

$offset = 6 + ($streams.Count * 16)

foreach ($item in $streams) {
    $w = if ($item.Size -ge 256) { [byte]0 } else { [byte]$item.Size }
    $h = $w
    $bw.Write($w)
    $bw.Write($h)
    $bw.Write([byte]0) # Color count
    $bw.Write([byte]0) # Reserved
    $bw.Write([System.Int16]1) # Color planes
    $bw.Write([System.Int16]32) # Bits per pixel
    $bw.Write([System.Int32]$item.Bytes.Length) # Image size in bytes
    $bw.Write([System.Int32]$offset) # Offset
    $offset += $item.Bytes.Length
}

foreach ($item in $streams) {
    $bw.Write($item.Bytes)
}

$bw.Flush()
$fs.Dispose()
Write-Host "Created high-res ICO at: $dest"
