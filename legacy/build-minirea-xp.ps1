param(
    [string]$ToolchainBin = (Join-Path $PSScriptRoot '..\tools\win-x86-build\w64devkit\bin'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist\BoltWorks-Mini-REA-XP-0.1')
)

$ErrorActionPreference = 'Stop'
$toolchain = (Resolve-Path -LiteralPath $ToolchainBin).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$previousPath = $env:Path
try {
    $env:Path = "$toolchain;$previousPath"
    $gcc = Join-Path $toolchain 'gcc.exe'
    $target = & $gcc -dumpmachine
    if ($LASTEXITCODE -ne 0 -or $target -ne 'i686-w64-mingw32') {
        throw 'Use the x86 MinGW-w64 toolchain.'
    }
    $options = @(
        '-std=c11','-Os','-Wall','-Wextra','-march=i586','-mfpmath=387','-mno-sse','-mno-sse2',
        '-D_WIN32_WINNT=0x0501','-DWINVER=0x0501','-D__USE_MINGW_ANSI_STDIO=0',
        '-ffunction-sections','-fdata-sections','-static','-s','-municode',
        '-Wl,--gc-sections,--major-os-version,5,--minor-os-version,1,--major-subsystem-version,5,--minor-subsystem-version,1',
        (Join-Path $PSScriptRoot 'minirea-xp.c'),'-o',(Join-Path $output 'MINIREA.EXE')
    )
    & $gcc @options
    if ($LASTEXITCODE -ne 0) { throw 'Mini REA XP compilation failed.' }
} finally {
    $env:Path = $previousPath
}
