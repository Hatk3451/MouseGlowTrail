# Turns frame folders into GIFs with ffmpeg (palette per animation, error-diffusion dithering).
#   demo frames:  dotnet bin\Release\net10.0-windows\MouseGlowTrail.Bench.dll demo out\demo
#   then:         .\make-gifs.ps1 -Frames out\demo -Out ..\..\docs\images
param(
    [Parameter(Mandatory)][string]$Frames,
    [Parameter(Mandatory)][string]$Out,
    [int]$Fps = 30,
    [string]$Ffmpeg = 'ffmpeg'
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $Out -Force | Out-Null
foreach ($folder in Get-ChildItem -LiteralPath $Frames -Directory) {
    $target = Join-Path $Out ($folder.Name + '.gif')
    $filter = 'split[a][b];[a]palettegen=max_colors=256:stats_mode=full[p];[b][p]paletteuse=dither=sierra2_4a'
    & $Ffmpeg -hide_banner -loglevel error -y -framerate $Fps -i (Join-Path $folder.FullName 'f%04d.png') `
        -vf $filter -loop 0 $target
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed for $($folder.Name)" }
    '{0,-12} {1,8:N0} KB' -f $folder.Name, ((Get-Item $target).Length / 1KB)
}
