[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Distribution)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
$previousToken = $env:GH_TOKEN
try {
    $folder = (Resolve-Path -LiteralPath $Distribution).Path
    $manifest = Get-Content -LiteralPath (Join-Path $folder 'update.json') -Raw | ConvertFrom-Json
    $installer = Join-Path $folder $manifest.installerName
    if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $manifest.sha256 -or
        (Get-Item -LiteralPath $installer).Length -ne $manifest.size) {
        throw 'Installer does not match update.json.'
    }
    if (git status --porcelain) { throw 'Commit the validated source before publishing.' }
    $commit = git rev-parse HEAD
    $remote = git ls-remote origin refs/heads/main
    if ($LASTEXITCODE -ne 0 -or ($remote -split '\s+')[0] -ne $commit) {
        throw 'Push this commit to main before publishing.'
    }
    if (-not $env:GH_TOKEN) {
        & gh auth status *> $null
        if ($LASTEXITCODE -ne 0) {
            $previousPrompt = $env:GIT_TERMINAL_PROMPT
            $previousInteractive = $env:GCM_INTERACTIVE
            try {
                $env:GIT_TERMINAL_PROMPT = '0'
                $env:GCM_INTERACTIVE = 'never'
                $credential = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
                if ($LASTEXITCODE -ne 0) { throw 'Sign into GitHub CLI or Git Credential Manager before publishing.' }
                $password = $credential | Where-Object { $_.StartsWith('password=') } | Select-Object -First 1
                if (-not $password) { throw 'GitHub credential is unavailable.' }
                $env:GH_TOKEN = $password.Substring(9)
            }
            finally {
                $env:GIT_TERMINAL_PROMPT = $previousPrompt
                $env:GCM_INTERACTIVE = $previousInteractive
            }
        }
    }
    $tag = "v$($manifest.version)"
    $notes = Join-Path $folder 'release-notes.txt'
    if (-not (Test-Path -LiteralPath $notes)) { throw 'Provide release-notes.txt in the distribution folder.' }
    $assets = @($installer, (Join-Path $folder 'update.json'), (Join-Path $folder 'SHA256SUMS.txt'))
    $assets += @(Get-ChildItem -LiteralPath $folder -Filter 'BIM-Tools-*.zip' -File | ForEach-Object FullName)
    & gh release create $tag --repo Michael-Mah/BIM-Tools --target $commit --draft --title "BIM Tools $($manifest.version)" --notes-file $notes
    if ($LASTEXITCODE -ne 0) { throw 'Could not create release draft. Existing releases are retained.' }
    & gh release upload $tag @assets --repo Michael-Mah/BIM-Tools
    if ($LASTEXITCODE -ne 0) { throw 'Upload failed. The release remains a draft.' }
    & gh release edit $tag --repo Michael-Mah/BIM-Tools --draft=false --latest
    if ($LASTEXITCODE -ne 0) { throw 'Could not publish release draft.' }
}
finally {
    $env:GH_TOKEN = $previousToken
    Pop-Location
}
