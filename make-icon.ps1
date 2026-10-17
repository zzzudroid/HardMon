# Генерирует app.ico (16/24/32/48/64/256, PNG внутри ICO): тёмный квадрат с мини-графиком в стиле виджета.
Add-Type -AssemblyName System.Drawing
Set-Location $PSScriptRoot

function New-IconPng([int]$size) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $s = $size / 32.0

    # Фон — скруглённый квадрат
    $r = 6 * $s; $path = New-Object Drawing.Drawing2D.GraphicsPath
    $w = $size - 1
    $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90); $path.AddArc($w - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($w - 2 * $r, $w - 2 * $r, 2 * $r, 2 * $r, 0, 90); $path.AddArc(0, $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 30, 30, 30))), $path)
    $g.DrawPath((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255, 17, 145, 216)), ([Math]::Max(1, 1.5 * $s))), $path)

    # График: заливка + линия
    $pts = @(@(4, 24), @(9, 20), @(13, 22), @(17, 12), @(21, 16), @(25, 7), @(28, 10)) | ForEach-Object { New-Object Drawing.PointF ($_[0] * $s), ($_[1] * $s) }
    $fill = New-Object Drawing.Drawing2D.GraphicsPath
    $fill.AddLines([Drawing.PointF[]]$pts)
    $fill.AddLine($pts[-1], (New-Object Drawing.PointF (28 * $s), (27 * $s)))
    $fill.AddLine((New-Object Drawing.PointF (28 * $s), (27 * $s)), (New-Object Drawing.PointF (4 * $s), (27 * $s)))
    $fill.CloseFigure()
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(110, 156, 204, 101))), $fill)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255, 156, 204, 101)), ([Math]::Max(1.2, 2 * $s))
    $pen.LineJoin = 'Round'
    $g.DrawLines($pen, [Drawing.PointF[]]$pts)

    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Маленькие размеры — классический DIB (BGRA снизу вверх + AND-маска), его понимают все загрузчики
        $w = New-Object IO.BinaryWriter $ms
        $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2))
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
        $w.Write([uint32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
            }
        }
        $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
        $w.Write((New-Object byte[] ($maskRow * $size)))
        $w.Flush()
    }
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$images = $sizes | ForEach-Object { ,(New-IconPng $_) }

$out = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img) }
[IO.File]::WriteAllBytes("$PSScriptRoot\app.ico", $out.ToArray())
Write-Host 'app.ico создан'
