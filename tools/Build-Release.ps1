[CmdletBinding()]
param(
    [ValidateSet(2024, 2025, 2026, 2027)]
    [int[]] $Versions = @(2024, 2025, 2026, 2027),
    # Optional directory containing a 2024/2025/2026/2027 subfolder with matching Autodesk DLLs.
    [string] $ReferenceRoot,
    [string] $FrameworkReferenceRoot,
    [string] $MSBuildPath = "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/MSBuild.exe"
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$Versions = @($Versions | Sort-Object -Unique)
if ($Versions.Count -eq 0) { throw 'Select at least one Navisworks version.' }
if (-not (Test-Path -LiteralPath $MSBuildPath)) { throw "MSBuild not found: $MSBuildPath" }

# Validate every requested year before building or changing a distribution.
$referenceDirs = @{}
foreach ($year in $Versions) {
    $referenceDir = if ($ReferenceRoot) { Join-Path $ReferenceRoot $year.ToString() }
        else { Join-Path $env:ProgramFiles "Autodesk/Navisworks Manage $year" }
    foreach ($assemblyName in @('Autodesk.Navisworks.Api', 'Autodesk.Navisworks.Clash', 'Autodesk.Navisworks.ComApi', 'Autodesk.Navisworks.Interop.ComApi')) {
        $assemblyFile = Join-Path $referenceDir ($assemblyName + '.dll')
        if (-not (Test-Path -LiteralPath $assemblyFile)) {
            throw "Missing $year reference: $assemblyFile. Use -ReferenceRoot for authentic matching Autodesk assemblies, or -Versions to explicitly build a subset."
        }
        $identity = [Reflection.AssemblyName]::GetAssemblyName($assemblyFile)
        $expectedMajor = $year - 2003
        if ($identity.Name -ne $assemblyName -or $identity.Version.ToString() -ne "$expectedMajor.0.0.0") {
            throw "Wrong reference for Navisworks ${year}: $identity"
        }
    }
    $referenceDirs[$year] = (Resolve-Path -LiteralPath $referenceDir).Path
}

# Use reference assemblies, never silently build against runtime/GAC assemblies.
$developerPack = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8/mscorlib.dll'
if (-not $FrameworkReferenceRoot -and -not (Test-Path -LiteralPath $developerPack)) {
    $packageDir = Join-Path $repoRoot '.deps/net48'
    $FrameworkReferenceRoot = Join-Path $packageDir 'build'
    if (-not (Test-Path -LiteralPath (Join-Path $FrameworkReferenceRoot '.NETFramework/v4.8/mscorlib.dll'))) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $packageDir) -Force | Out-Null
        $packageZip = Join-Path $repoRoot '.deps/net48-reference-assemblies.zip'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net48/1.0.3/microsoft.netframework.referenceassemblies.net48.1.0.3.nupkg' -OutFile $packageZip
        Expand-Archive -LiteralPath $packageZip -DestinationPath $packageDir -Force
    }
}
if ($FrameworkReferenceRoot) {
    $FrameworkReferenceRoot = (Resolve-Path -LiteralPath $FrameworkReferenceRoot).Path + [IO.Path]::DirectorySeparatorChar
    if (-not (Test-Path -LiteralPath (Join-Path $FrameworkReferenceRoot '.NETFramework/v4.8/mscorlib.dll'))) {
        throw 'FrameworkReferenceRoot must contain .NETFramework/v4.8 reference assemblies.'
    }
}

foreach ($year in $Versions) {
    $buildArgs = @(
        (Join-Path $repoRoot 'src/BIM.Tools/BIM.Tools.csproj'),
        '/t:Rebuild', '/p:Configuration=Release', '/p:Platform=x64',
        "/p:NavisworksVersion=$year", "/p:NavisworksInstallDir=$($referenceDirs[$year])", '/verbosity:minimal'
    )
    if ($FrameworkReferenceRoot) { $buildArgs += "/p:FrameworkReferenceRoot=$FrameworkReferenceRoot" }
    & $MSBuildPath @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Navisworks $year build failed." }
    $converterCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $converterOutput=Join-Path $repoRoot "bundle/BIM.Tools.bundle/Contents/$year/BIM.NativeSharingConverter.exe"
    & $converterCompiler /nologo /target:exe /platform:x64 "/out:$converterOutput" /reference:System.dll /reference:System.Core.dll "/reference:$($referenceDirs[$year])/Autodesk.Navisworks.Automation.dll" "/reference:$($referenceDirs[$year])/Autodesk.Navisworks.Api.dll" (Join-Path $repoRoot 'Shared/Tools/NativeSharingConverter.cs')
    if($LASTEXITCODE -ne 0) {throw "Native sharing converter build failed for $year"}
}

# A subset package advertises ONLY the years actually built in this run.
[xml] $manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'bundle/BIM.Tools.bundle/PackageContents.xml') -Raw
foreach ($component in @($manifest.ApplicationPackage.Components)) {
    $year = [int] ($component.ComponentEntry.ModuleName -split '/')[2]
    if ($Versions -notcontains $year) { $null = $manifest.ApplicationPackage.RemoveChild($component) }
}
$releaseVersion = $manifest.ApplicationPackage.AppVersion
$manifest.ApplicationPackage.FriendlyVersion = "$releaseVersion Navisworks Manage " + ($Versions -join '/')
$releaseName = "BIM-Tools-$releaseVersion-" + ($Versions -join '-')
$distributionRoot = Join-Path $repoRoot 'dist'
$distributionDir = Join-Path $distributionRoot $releaseName
$packageFolder = Join-Path $distributionDir 'BIM.Tools.bundle'
# A fresh staging folder avoids stale files from an older subset or failed build.
if (Test-Path -LiteralPath $distributionDir) {
    $distributionDir += '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $packageFolder = Join-Path $distributionDir 'BIM.Tools.bundle'
}
New-Item -ItemType Directory -Path (Join-Path $packageFolder 'Contents') -Force | Out-Null
foreach ($year in $Versions) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "bundle/BIM.Tools.bundle/Contents/$year") -Destination (Join-Path $packageFolder 'Contents') -Recurse
}
$manifest.Save((Join-Path $packageFolder 'PackageContents.xml'))

Add-Type -AssemblyName System.IO.Compression.FileSystem
$payloadZip = Join-Path $distributionDir 'bundle-payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($packageFolder, $payloadZip)
$setupExe = Join-Path $distributionDir "BIM-Tools-$releaseVersion-Setup.exe"
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:winexe /platform:x64 "/out:$setupExe" "/win32manifest:$(Join-Path $repoRoot 'installer/app.manifest')" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/resource:$payloadZip,BIM.Tools.Bundle.zip" (Join-Path $repoRoot 'installer/Setup.cs') (Join-Path $repoRoot 'Shared/UI/BimWindowLayout.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

$packageZip = Join-Path $distributionDir ($releaseName + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($packageFolder, $packageZip)
$files = @(Get-ChildItem -LiteralPath $distributionDir -File | Where-Object Name -ne 'bundle-payload.zip')
$hashes = foreach ($file in $files) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash, $file.Name
}
$hashes | Set-Content -LiteralPath (Join-Path $distributionDir 'SHA256SUMS.txt') -Encoding UTF8
Write-Output "Release built for Navisworks Manage: $($Versions -join ', ')"
Write-Output "Distribution: $distributionDir"
