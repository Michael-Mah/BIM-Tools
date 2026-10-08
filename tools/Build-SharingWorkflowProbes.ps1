[CmdletBinding()]
param([ValidateSet(2026,2027)] [int[]] $Versions = @(2026,2027))
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
# Compile the production services/UI under an isolated test assembly identity.
# A running user's old bundle is never overwritten or attached to automation.
foreach ($year in $Versions) {
    $apiDir = Join-Path $env:ProgramFiles "Autodesk/Navisworks Manage $year"
    $probeDir = Join-Path $PSScriptRoot "probe-bin/workflow/$year"
    New-Item -ItemType Directory -Path $probeDir -Force | Out-Null
    $compilerArgs = @('/nologo','/target:library','/platform:x64',
        "/out:$probeDir/BIM.WorkflowIsolated.dll", "/define:NAVISWORKS$year",
        '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll','/reference:System.IO.Compression.dll',
        '/reference:System.IO.Compression.FileSystem.dll','/reference:System.Xml.dll',
        "/resource:$repoRoot/src/BIM.Tools/Ribbon/Images/BimModelSharing64.png,BIM.ModelSharingIcon")
    foreach ($name in @('Api','Clash','ComApi','Interop.ComApi')) { $compilerArgs += "/reference:$apiDir/Autodesk.Navisworks.$name.dll" }
    $compilerArgs += @(Get-ChildItem (Join-Path $repoRoot 'src/BIM.Tools') -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(Plugin|obj|bin)\\' } | ForEach-Object FullName)
    $compilerArgs += @(Get-ChildItem (Join-Path $repoRoot 'Shared') -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '\\Tools\\' } | ForEach-Object FullName)
    $compilerArgs += @('SharingWorkflowProbePlugin.cs','ScopedSharingProbePlugin.cs','DpiUiProbePlugin.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ })
    & $compiler @compilerArgs
    if ($LASTEXITCODE -ne 0) { throw "Sharing source probe compilation failed for $year" }
    & $compiler /nologo /target:exe /platform:x64 "/out:$probeDir/SharingWorkflowRunner.exe" "/reference:$apiDir/Autodesk.Navisworks.Automation.dll" (Join-Path $PSScriptRoot 'SharingWorkflowRunner.cs')
    if ($LASTEXITCODE -ne 0) { throw "Workflow runner compilation failed for $year" }
    & $compiler /nologo /target:exe /platform:x64 "/out:$probeDir/BIM.NativeSharingConverter.exe" /reference:System.dll /reference:System.Core.dll "/reference:$apiDir/Autodesk.Navisworks.Automation.dll" "/reference:$apiDir/Autodesk.Navisworks.Api.dll" (Join-Path $repoRoot 'Shared/Tools/NativeSharingConverter.cs')
    if($LASTEXITCODE -ne 0) {throw "Native sharing converter compilation failed for $year"}
}
