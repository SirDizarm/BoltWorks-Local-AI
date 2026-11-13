param(
    [string]$OutputDirectory,
    [ValidateSet('x64','x86')][string]$Platform = 'x64'
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskDist = if($OutputDirectory) {$OutputDirectory} elseif($Platform -eq 'x86') {Join-Path $taskRoot 'dist\BoltWorks-Local-AI-x86'} else {Join-Path $taskRoot 'dist\BoltWorks-Local-AI'}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
New-Item -ItemType Directory -Path $taskDist -Force | Out-Null
$taskSources=@(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' -File | ForEach-Object {$_.FullName})
& $compiler /nologo /target:winexe /main:BoltWorks.LlamaLite.Program /platform:$Platform /optimize+ /out:"$taskDist\BoltWorks-Local-AI.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
& $compiler /nologo /target:exe /main:BoltWorks.LlamaLite.Cli /platform:$Platform /optimize+ /out:"$taskDist\BoltWorks-Local-AI-CLI.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll @taskSources
if ($LASTEXITCODE -ne 0) { throw 'CLI compilation failed' }
Write-Output "Built $taskDist\BoltWorks-Local-AI.exe. Keep the supplied runtime folder beside it."
Write-Output "Built $taskDist\BoltWorks-Local-AI-CLI.exe. Run it with --help for script integration."
$miniReaPlatform = if($Platform -eq 'x86') {'x86'} else {'anycpu'}
& $compiler /nologo /target:exe /main:BoltWorks.LlamaLite.MiniReaCli /platform:$miniReaPlatform /optimize+ /out:"$taskDist\mini-rea.exe" (Join-Path $taskRoot 'src\MiniRea.cs') (Join-Path $taskRoot 'src\MiniReaNetwork.cs') (Join-Path $taskRoot 'src\PeInspector.cs') (Join-Path $taskRoot 'src\ResourceEditor.cs') (Join-Path $taskRoot 'src\DecompilerRunner.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mini REA compilation failed' }
