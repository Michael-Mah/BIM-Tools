[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Distribution,
    [ValidateSet(2024,2025,2027)][int]$HostVersion = 2024,
    [switch]$Live
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$distributionFolder = (Resolve-Path -LiteralPath $Distribution).Path
$output = Join-Path $repo ('tmp/update-tests-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$api = Join-Path $env:ProgramFiles "Autodesk/Navisworks Manage $HostVersion"
$sources = @(
    (Join-Path $repo 'src/BIM.Tools/Updates/UpdateClient.cs'),
    (Join-Path $repo 'src/BIM.Tools/EditionBrand.cs'),
    (Join-Path $PSScriptRoot 'UpdateTests.cs')
)
foreach ($current in $false,$true) {
    $exe = Join-Path $output ("UpdateTests-$current.exe")
    $arguments = @('/nologo','/target:exe','/platform:x64',"/out:$exe",
        '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll',
        '/reference:System.Security.dll','/reference:System.Web.Extensions.dll','/reference:System.Xml.dll',
        "/reference:$api/Autodesk.Navisworks.Api.dll")
    if ($current) { $arguments += '/define:CURRENT_VERSION' }
    & $compiler @arguments $sources
    if ($LASTEXITCODE -ne 0) { throw 'Update test compilation failed' }
    if ($Live -and -not $env:BIM_UPDATE_TEST_TOKEN) { throw 'Live checks require BIM_UPDATE_TEST_TOKEN in this process environment.' }
    & $exe $api (Join-Path $distributionFolder 'update.json')
    if ($LASTEXITCODE -ne 0) { throw 'Update checks failed' }
}
