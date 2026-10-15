[CmdletBinding()]
param([Parameter(Mandatory=$true)][string[]] $RibbonRoots)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore
$checked = 0
foreach ($root in $RibbonRoots) {
    [xml] $ribbon = Get-Content -LiteralPath (Join-Path $root 'BimToolsRibbon.xaml') -Raw
    $buttons = @($ribbon.SelectNodes('//*[@Image or @LargeImage]'))
    if ($buttons.Count -ne 3) { throw "Expected three tool buttons in $root" }
    foreach ($button in $buttons) {
        foreach ($attribute in @('Image','LargeImage')) {
            $relative = $button.GetAttribute($attribute)
            if ($relative -notmatch '(16|32)\.png$') { throw "Unexpected ribbon icon reference: $relative" }
            $expectedSize = [int] $matches[1]
            $file = Join-Path $root $relative
            $stream = [IO.File]::OpenRead($file)
            try {
                $decoder = [Windows.Media.Imaging.BitmapDecoder]::Create($stream,
                    [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
                    [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
                $frame = $decoder.Frames[0]
                # Check the same physical size calculation used by the WPF ribbon.
                if ($frame.PixelWidth -ne $expectedSize -or $frame.PixelHeight -ne $expectedSize -or
                    [Math]::Abs($frame.Width - $expectedSize) -gt 0.05 -or
                    [Math]::Abs($frame.Height - $expectedSize) -gt 0.05 -or
                    [Math]::Abs($frame.DpiX - 96) -gt 0.05 -or
                    [Math]::Abs($frame.DpiY - 96) -gt 0.05) {
                    throw "Ribbon icon would render at the wrong size: $file ($($frame.Width) x $($frame.Height) DIP; $($frame.DpiX) DPI)"
                }
                $checked++
            }
            finally { $stream.Dispose() }
        }
    }
}
Write-Output "PASS: $checked ribbon references decode to their intended WPF dimensions at 96 DPI."
