# Native vector-style icon, rendered at each Windows icon size without external tools.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$assets=Join-Path $PSScriptRoot '..\src\PCStatusBar\Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
# x, y, width, height, corner radius, fill. Coordinates use a 64-unit canvas.
$shapes=@(
    @(2,2,60,60,15,'#171E2B'),
    @(12,12,40,36,7,'#29364B'),
    @(16,16,32,28,4,'#101722'),
    @(20,30,6,10,2,'#70E0B5'),
    @(29,21,6,19,2,'#61C9F2'),
    @(38,26,6,14,2,'#F6CD72'),
    @(24,51,16,4,2,'#A5B9CF')
)
$svg=@('<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 64 64">')
foreach($s in $shapes){$svg+='<rect x="'+$s[0]+'" y="'+$s[1]+'" width="'+$s[2]+'" height="'+$s[3]+'" rx="'+$s[4]+'" fill="'+$s[5]+'"/>'}
$svg+='</svg>'
[IO.File]::WriteAllLines((Join-Path $assets 'app.svg'),$svg)
function Render([int]$size){
    $bmp=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics=[Drawing.Graphics]::FromImage($bmp)
    try{
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size/64.0,$size/64.0)
        foreach($s in $shapes){
            $x=[single]$s[0];$y=[single]$s[1];$w=[single]$s[2];$h=[single]$s[3];$d=[single]($s[4]*2)
            $path=[Drawing.Drawing2D.GraphicsPath]::new()
            $brush=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($s[5]))
            try{
                $path.AddArc($x,$y,$d,$d,180,90);$path.AddArc($x+$w-$d,$y,$d,$d,270,90)
                $path.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90);$path.AddArc($x,$y+$h-$d,$d,$d,90,90)
                $path.CloseFigure();$graphics.FillPath($brush,$path)
            }finally{$path.Dispose();$brush.Dispose()}
        }
    }finally{$graphics.Dispose()}
    return ,$bmp
}
$sizes=@(16,20,24,32,40,48,64,128,256)
$frames=@(foreach($size in $sizes){
    $bmp=Render $size;$stream=[IO.MemoryStream]::new()
    try{$bmp.Save($stream,[Drawing.Imaging.ImageFormat]::Png); ,$stream.ToArray()}
    finally{$bmp.Dispose();$stream.Dispose()}
})
$file=[IO.File]::Create((Join-Path $assets 'app.ico'));$writer=[IO.BinaryWriter]::new($file)
try{
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
    $offset=6+16*$sizes.Count
    for($i=0;$i -lt $sizes.Count;$i++){
        $edge=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
        $writer.Write([byte]$edge);$writer.Write([byte]$edge);$writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset)
        $offset+=$frames[$i].Length
    }
    foreach($frame in $frames){$writer.Write([byte[]]$frame)}
}finally{$writer.Dispose();$file.Dispose()}
$preview=Render 512
try{$preview.Save((Join-Path $assets 'app.png'),[Drawing.Imaging.ImageFormat]::Png)}finally{$preview.Dispose()}
Write-Output 'Generated app.svg, app.png and app.ico (16–256 px).'
