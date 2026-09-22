#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'agent-service-install-files.ps1')

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("GaltekAgentInstallFiles-{0}" -f [guid]::NewGuid().ToString('N'))
$artifactDirectory = Join-Path $testRoot 'artifact'
$installDirectory = Join-Path $testRoot 'install'

try {
    New-Item -ItemType Directory -Path $artifactDirectory, $installDirectory | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $installDirectory 'Session') | Out-Null
    Set-Content -LiteralPath (Join-Path $installDirectory 'appsettings.json') -Value '{"MasterConnection":{"Enabled":true,"Endpoint":"preserve-me"}}' -NoNewline
    Set-Content -LiteralPath (Join-Path $installDirectory 'obsolete.dll') -Value 'obsolete' -NoNewline
    Set-Content -LiteralPath (Join-Path $installDirectory 'Session\keep.txt') -Value 'keep' -NoNewline
    Set-Content -LiteralPath (Join-Path $artifactDirectory 'appsettings.json') -Value '{"MasterConnection":{"Enabled":false}}' -NoNewline
    Set-Content -LiteralPath (Join-Path $artifactDirectory 'agent.dll') -Value 'new' -NoNewline

    Install-AgentServiceArtifactFiles `
        -ArtifactDirectory $artifactDirectory `
        -InstallDirectory $installDirectory `
        -PreservedSubdirectories @('Session', 'CredentialProvider') `
        -PreservedFileNames @('appsettings.json')

    $effectiveConfig = Get-Content -Raw -LiteralPath (Join-Path $installDirectory 'appsettings.json')
    if ($effectiveConfig -notmatch 'preserve-me') { throw 'Existing appsettings.json was overwritten.' }
    if (-not (Test-Path -LiteralPath (Join-Path $installDirectory 'agent.dll') -PathType Leaf)) { throw 'Artifact file was not copied.' }
    if (Test-Path -LiteralPath (Join-Path $installDirectory 'obsolete.dll')) { throw 'Obsolete Service file was not removed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $installDirectory 'Session\keep.txt') -PathType Leaf)) { throw 'Preserved Session directory was changed.' }

    Write-Host 'Agent Service artifact preservation tests passed.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
