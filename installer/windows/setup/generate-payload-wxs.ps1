#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $PayloadRoot,
    [Parameter(Mandatory = $true)][string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-StableToken {
    param([Parameter(Mandatory = $true)][string] $Value)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant()))
        return ([BitConverter]::ToString($bytes, 0, 10)).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Get-StableGuid {
    param([Parameter(Mandatory = $true)][string] $Value)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("GaltekClassroom.Client.Component.v1|$($Value.ToLowerInvariant())"))
        $guidBytes = New-Object byte[] 16
        [Array]::Copy($bytes, $guidBytes, 16)
        $guidBytes[7] = ($guidBytes[7] -band 0x0F) -bor 0x40
        $guidBytes[8] = ($guidBytes[8] -band 0x3F) -bor 0x80
        return ([Guid]::new($guidBytes)).ToString('B').ToUpperInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Escape-Xml {
    param([AllowEmptyString()][string] $Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)][string] $BasePath,
        [Parameter(Mandatory = $true)][string] $Path
    )

    $baseUri = [Uri]((Resolve-Path -LiteralPath $BasePath).Path.TrimEnd('\') + '\')
    $pathUri = [Uri](Resolve-Path -LiteralPath $Path).Path
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function Write-DirectoryTree {
    param(
        [Parameter(Mandatory = $true)][System.Text.StringBuilder] $Builder,
        [Parameter(Mandatory = $true)][string] $RootId,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]] $RelativeDirectories,
        [Parameter(Mandatory = $true)][string] $Prefix
    )

    $tree = @{}
    foreach ($relativeDirectory in $RelativeDirectories) {
        $segments = @($relativeDirectory -split '\\' | Where-Object { $_ })
        $cursor = $tree
        foreach ($segment in $segments) {
            if (-not $cursor.ContainsKey($segment)) { $cursor[$segment] = @{} }
            $cursor = $cursor[$segment]
        }
    }

    function Write-Nodes {
        param($Nodes, [string]$CurrentPath, [int]$Depth)
        foreach ($name in @($Nodes.Keys | Sort-Object)) {
            $relative = if ([string]::IsNullOrWhiteSpace($CurrentPath)) { $name } else { "$CurrentPath\$name" }
            $id = "dir$Prefix$(Get-StableToken -Value $relative)"
            [void]$Builder.AppendLine((' ' * $Depth) + "<Directory Id=`"$id`" Name=`"$(Escape-Xml $name)`">")
            Write-Nodes -Nodes $Nodes[$name] -CurrentPath $relative -Depth ($Depth + 2)
            [void]$Builder.AppendLine((' ' * $Depth) + '</Directory>')
        }
    }

    [void]$Builder.AppendLine("    <DirectoryRef Id=`"$RootId`">")
    Write-Nodes -Nodes $tree -CurrentPath '' -Depth 6
    [void]$Builder.AppendLine('    </DirectoryRef>')
}

function Get-DirectoryId {
    param(
        [Parameter(Mandatory = $true)][string] $RootId,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $RelativeDirectory,
        [Parameter(Mandatory = $true)][string] $Prefix
    )

    if ([string]::IsNullOrWhiteSpace($RelativeDirectory) -or $RelativeDirectory -eq '.') { return $RootId }
    return "dir$Prefix$(Get-StableToken -Value $RelativeDirectory)"
}

$resolvedPayloadRoot = [System.IO.Path]::GetFullPath($PayloadRoot)
$serviceRoot = Join-Path $resolvedPayloadRoot 'Service'
$sessionRoot = Join-Path $resolvedPayloadRoot 'Session'
if (-not (Test-Path -LiteralPath $serviceRoot -PathType Container)) { throw "Service payload was not found: $serviceRoot" }
if (-not (Test-Path -LiteralPath $sessionRoot -PathType Container)) { throw "Session payload was not found: $sessionRoot" }

$items = @()
foreach ($definition in @(
    @{ Name = 'Service'; Root = $serviceRoot; DirectoryId = 'INSTALLFOLDER'; Prefix = 'Svc' },
    @{ Name = 'Session'; Root = $sessionRoot; DirectoryId = 'SessionDirectory'; Prefix = 'Ses' }
)) {
    foreach ($file in Get-ChildItem -LiteralPath $definition.Root -File -Recurse | Sort-Object FullName) {
        $relativePath = Get-RelativePathCompat -BasePath $definition.Root -Path $file.FullName
        if ($definition.Name -eq 'Service' -and $relativePath -ieq 'appsettings.json') { continue }
        $relativeDirectory = Split-Path -Parent $relativePath
        $logicalPath = "$($definition.Name)\$relativePath"
        $items += [pscustomobject]@{
            LogicalPath = $logicalPath
            SourcePath = $file.FullName
            FileName = $file.Name
            DirectoryId = Get-DirectoryId -RootId $definition.DirectoryId -RelativeDirectory $relativeDirectory -Prefix $definition.Prefix
            ComponentId = "cmp$(Get-StableToken -Value $logicalPath)"
            FileId = "fil$(Get-StableToken -Value $logicalPath)"
            ComponentGuid = Get-StableGuid -Value $logicalPath
            IsServiceExecutable = $logicalPath -ieq 'Service\GaltekClassroom.Agent.Service.exe'
        }
    }
}

$builder = [System.Text.StringBuilder]::new()
[void]$builder.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$builder.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$builder.AppendLine('  <Fragment>')

$serviceDirectories = @($items | Where-Object { $_.LogicalPath.StartsWith('Service\') } | ForEach-Object {
    $relative = $_.LogicalPath.Substring('Service\'.Length)
    Split-Path -Parent $relative
} | Where-Object { $_ } | Sort-Object -Unique)
$sessionDirectories = @($items | Where-Object { $_.LogicalPath.StartsWith('Session\') } | ForEach-Object {
    $relative = $_.LogicalPath.Substring('Session\'.Length)
    Split-Path -Parent $relative
} | Where-Object { $_ } | Sort-Object -Unique)

if ($serviceDirectories.Count -gt 0) {
    Write-DirectoryTree -Builder $builder -RootId 'INSTALLFOLDER' -RelativeDirectories $serviceDirectories -Prefix 'Svc'
}
if ($sessionDirectories.Count -gt 0) {
    Write-DirectoryTree -Builder $builder -RootId 'SessionDirectory' -RelativeDirectories $sessionDirectories -Prefix 'Ses'
}
[void]$builder.AppendLine('  </Fragment>')
[void]$builder.AppendLine('  <Fragment>')
[void]$builder.AppendLine('    <ComponentGroup Id="PublishedPayloadComponents">')

foreach ($item in $items) {
    [void]$builder.AppendLine("      <Component Id=`"$($item.ComponentId)`" Directory=`"$($item.DirectoryId)`" Guid=`"$($item.ComponentGuid)`" Bitness=`"always64`">")
    [void]$builder.AppendLine("        <File Id=`"$($item.FileId)`" Source=`"$(Escape-Xml $item.SourcePath)`" Name=`"$(Escape-Xml $item.FileName)`" KeyPath=`"yes`" Checksum=`"yes`" />")
    if ($item.IsServiceExecutable) {
        [void]$builder.AppendLine('        <ServiceControl Id="ctlPublishedGaltekAgent" Name="GaltekClassroomAgent" Stop="both" Wait="yes" />')
    }
    [void]$builder.AppendLine('      </Component>')
}

[void]$builder.AppendLine('    </ComponentGroup>')
[void]$builder.AppendLine('  </Fragment>')
[void]$builder.AppendLine('</Wix>')

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedOutput) -Force | Out-Null
[System.IO.File]::WriteAllText($resolvedOutput, $builder.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Output "Generated WiX payload fragment: $resolvedOutput"
Write-Output "Payload components: $($items.Count)"
