[CmdletBinding()]
param([ValidateSet(2024,2025,2026,2027)] [int[]]$Versions=@(2024,2025,2026,2027))
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
foreach($year in $Versions){
 $api=Join-Path $env:ProgramFiles "Autodesk/Navisworks Manage $year"
 $folder=Join-Path $repo "tools/probe-bin/audit/$year"
 New-Item -ItemType Directory -Path $folder -Force | Out-Null
 $arguments=@('/nologo','/target:library','/platform:x64',"/out:$folder/BIM.AuditIsolated.dll","/define:NAVISWORKS$year;AUDIT_PROBE",'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Xml.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll')
 foreach($name in @('Api','Clash','ComApi','Interop.ComApi')){$arguments+="/reference:$api/Autodesk.Navisworks.$name.dll"}
 $arguments+=@(Get-ChildItem (Join-Path $repo 'src/BIM.Tools') -Recurse -Filter '*.cs' | Where-Object{$_.FullName -notmatch '\\(Plugin|obj|bin)\\'} | ForEach-Object FullName)
 $arguments+=@(Get-ChildItem (Join-Path $repo 'Shared') -Recurse -Filter '*.cs' | Where-Object{$_.FullName -notmatch '\\Tools\\'} | ForEach-Object FullName)
 $arguments+=(Join-Path $PSScriptRoot 'AuditWorkflowProbe.cs')
 $arguments+=("/resource:"+(Join-Path $repo 'src/BIM.Tools/Ribbon/Images/BimFederationAudit64.png')+",BIM.FederationAuditIcon")
 & $compiler @arguments
 if($LASTEXITCODE -ne 0){throw "Audit native probe build failed for $year"}
 & $compiler /nologo /target:exe /platform:x64 "/out:$folder/AuditWorkflowRunner.exe" "/reference:$api/Autodesk.Navisworks.Automation.dll" (Join-Path $PSScriptRoot 'AuditWorkflowRunner.cs')
 if($LASTEXITCODE -ne 0){throw "Audit runner build failed for $year"}
}
