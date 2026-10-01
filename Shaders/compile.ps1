# Rebuilds Shaders/sonnet_final.bin from sonnet_final.hlsl.
#
# Uses the Windows SDK's fxc.exe exactly the way Win2D's PixelShaderEffect
# documentation requires: compile the entry point to a shader library, then
# link it into a full shader that carries the Direct2D reflection metadata.

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$fxc = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\fxc.exe" -ErrorAction SilentlyContinue |
    Sort-Object -Property FullName -Descending | Select-Object -First 1
if (-not $fxc) { throw 'fxc.exe not found; install the Windows SDK.' }

$include = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\Include\*\um\d2d1effecthelpers.hlsli" -ErrorAction SilentlyContinue |
    Sort-Object -Property FullName -Descending | Select-Object -First 1
if (-not $include) { throw 'd2d1effecthelpers.hlsli not found; install the Windows SDK.' }
$includeDir = Split-Path -Parent $include.FullName

$shader = Join-Path $here 'sonnet_final.hlsl'
$fxlib = Join-Path $here 'sonnet_final.fxlib'
$bin = Join-Path $here 'sonnet_final.bin'

& $fxc.FullName $shader /nologo /T lib_4_0 /D D2D_FUNCTION /D D2D_ENTRY=main `
    /Fl $fxlib /I $includeDir
if ($LASTEXITCODE -ne 0) { throw 'fxc library pass failed' }

& $fxc.FullName $shader /nologo /T ps_4_0 /D D2D_FULL_SHADER /D D2D_ENTRY=main /E main `
    /setprivate $fxlib /Fo $bin /I $includeDir
if ($LASTEXITCODE -ne 0) { throw 'fxc link pass failed' }

Remove-Item $fxlib -ErrorAction SilentlyContinue
Write-Host "wrote $bin"
