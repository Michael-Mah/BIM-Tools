[CmdletBinding()]
param([ValidateSet(2024,2025,2026,2027)] [int[]] $Versions = @(2024,2025,2026,2027))
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$probeRoot=Join-Path $PSScriptRoot 'probe-bin/clash'
New-Item -ItemType Directory -Path $probeRoot -Force | Out-Null
$core=@('ClashProfile.cs','ClashModelMatcher.cs','ClashProfileStore.cs' | ForEach-Object {Join-Path $repoRoot "src/BIM.Tools/Clash/$_"})
& $compiler /nologo /target:exe /platform:x64 "/out:$probeRoot/ClashProfileTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Drawing.dll $core (Join-Path $repoRoot 'src/BIM.Tools/EditionBrand.cs') (Join-Path $PSScriptRoot 'ClashProfileTests.cs')
if($LASTEXITCODE -ne 0){throw 'Profile test compilation failed'}
foreach($year in $Versions){
  $api=Join-Path $env:ProgramFiles "Autodesk/Navisworks Manage $year"
  $folder=Join-Path $probeRoot $year.ToString()
  New-Item -ItemType Directory -Path $folder -Force | Out-Null
  $compilerArgs=@('/nologo','/target:library','/platform:x64',"/out:$folder/BIM.ClashIsolated.dll","/define:NAVISWORKS$year",'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Xml.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll')
  foreach($name in @('Api','Clash','ComApi','Interop.ComApi')){$compilerArgs+="/reference:$api/Autodesk.Navisworks.$name.dll"}
  $compilerArgs+=@(Get-ChildItem (Join-Path $repoRoot 'src/BIM.Tools') -Recurse -Filter '*.cs' | Where-Object {$_.FullName -notmatch '\\(Plugin|obj|bin)\\'} | ForEach-Object FullName)
  $compilerArgs+=@(Get-ChildItem (Join-Path $repoRoot 'Shared') -Recurse -Filter '*.cs' | Where-Object {$_.FullName -notmatch '\\Tools\\'} | ForEach-Object FullName)
  $compilerArgs+=(Join-Path $PSScriptRoot 'ClashWorkflowProbePlugin.cs')
  & $compiler @compilerArgs
  if($LASTEXITCODE -ne 0){throw "Native probe compilation failed: $year"}
  & $compiler /nologo /target:exe /platform:x64 "/out:$folder/ClashWorkflowRunner.exe" "/reference:$api/Autodesk.Navisworks.Automation.dll" (Join-Path $PSScriptRoot 'ClashWorkflowRunner.cs')
  if($LASTEXITCODE -ne 0){throw "Runner compilation failed: $year"}
}
