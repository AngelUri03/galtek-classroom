#Requires -Version 5.1
Set-StrictMode -Version Latest

function Install-AgentServiceArtifactFiles {
    param(
        [Parameter(Mandatory = $true)][string] $ArtifactDirectory,
        [Parameter(Mandatory = $true)][string] $InstallDirectory,
        [Parameter(Mandatory = $true)][string[]] $PreservedSubdirectories,
        [Parameter(Mandatory = $true)][string[]] $PreservedFileNames
    )

    if (-not (Test-Path -LiteralPath $InstallDirectory)) {
        New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
    }

    foreach ($item in Get-ChildItem -LiteralPath $InstallDirectory -Force) {
        $preserveDirectory = $item.PSIsContainer -and ($PreservedSubdirectories | Where-Object {
            [string]::Equals($item.Name, $_, [System.StringComparison]::OrdinalIgnoreCase)
        })
        $preserveFile = -not $item.PSIsContainer -and ($PreservedFileNames | Where-Object {
            [string]::Equals($item.Name, $_, [System.StringComparison]::OrdinalIgnoreCase)
        })
        if ($preserveDirectory -or $preserveFile) {
            continue
        }

        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    }

    foreach ($sourceItem in Get-ChildItem -LiteralPath $ArtifactDirectory -Force) {
        $destination = Join-Path $InstallDirectory $sourceItem.Name
        $preserveExistingDirectory = $sourceItem.PSIsContainer `
            -and (Test-Path -LiteralPath $destination -PathType Container) `
            -and ($PreservedSubdirectories | Where-Object {
                [string]::Equals($sourceItem.Name, $_, [System.StringComparison]::OrdinalIgnoreCase)
            })
        $preserveExistingFile = -not $sourceItem.PSIsContainer `
            -and (Test-Path -LiteralPath $destination -PathType Leaf) `
            -and ($PreservedFileNames | Where-Object {
                [string]::Equals($sourceItem.Name, $_, [System.StringComparison]::OrdinalIgnoreCase)
            })
        if ($preserveExistingDirectory -or $preserveExistingFile) {
            continue
        }

        Copy-Item -LiteralPath $sourceItem.FullName -Destination $destination -Recurse -Force
    }
}
