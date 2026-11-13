param([switch]$Restore,[string]$OutputDirectory=(Join-Path $PSScriptRoot '..\..\dist\BoltWorks-Local-AI\decompiler'))
$ErrorActionPreference='Stop'
$project=Join-Path $PSScriptRoot 'Decompiler.csproj'
if($Restore){
    & dotnet restore $project --locked-mode --packages (Join-Path $PSScriptRoot '..\..\tools\nuget-cache') -p:NuGetAudit=false
    if($LASTEXITCODE -ne 0){throw 'Pinned backend restore failed'}
}
& dotnet publish $project --no-restore -c Release -o $OutputDirectory
if($LASTEXITCODE -ne 0){throw 'Backend publish failed'}
Write-Output 'Keep the full published folder and third-party license files together. No SDK/runtime installation is needed on the destination PC.'
