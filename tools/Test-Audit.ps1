[CmdletBinding()]
param([string]$OutputRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not $OutputRoot){$OutputRoot=Join-Path $repo ('tmp/audit-tests-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$files=@('Health/AuditData.cs','Health/AuditStore.cs','Health/AuditComparison.cs','Health/AuditListComparison.cs','Clash/ClashModelMatcher.cs','Clash/ClashProfile.cs','EditionBrand.cs' | ForEach-Object{Join-Path $repo ('src/BIM.Tools/'+$_)})
$executable=Join-Path $OutputRoot 'AuditTests.exe'
& $compiler /nologo /target:exe /platform:x64 "/out:$executable" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Drawing.dll $files (Join-Path $PSScriptRoot 'AuditTests.cs')
if($LASTEXITCODE -ne 0){throw 'Audit test compilation failed.'}
& $executable (Join-Path $OutputRoot 'saved-lists')
if($LASTEXITCODE -ne 0){throw 'Audit checks failed.'}
