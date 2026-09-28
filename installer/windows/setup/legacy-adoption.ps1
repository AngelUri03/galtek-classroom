#Requires -Version 5.1
Set-StrictMode -Version Latest

function ConvertFrom-GaltekWindowsCommandLineExecutable {
    param([AllowNull()][string] $CommandLine)

    if ([string]::IsNullOrWhiteSpace($CommandLine)) {
        return [pscustomobject]@{ IsValid = $false; ExecutablePath = $null; Arguments = $null; Reason = 'MISSING_IMAGE_PATH' }
    }

    $text = $CommandLine.Trim()
    $value = [Text.StringBuilder]::new()
    $insideQuotes = $false
    $index = 0

    while ($index -lt $text.Length) {
        if (-not $insideQuotes -and [char]::IsWhiteSpace($text[$index])) { break }

        $backslashes = 0
        while ($index -lt $text.Length -and $text[$index] -eq '\') {
            $backslashes++
            $index++
        }

        if ($index -lt $text.Length -and $text[$index] -eq '"') {
            [void]$value.Append('\', [int][Math]::Floor($backslashes / 2))
            if (($backslashes % 2) -eq 0) {
                $insideQuotes = -not $insideQuotes
            }
            else {
                [void]$value.Append('"')
            }
            $index++
            continue
        }

        [void]$value.Append('\', $backslashes)
        if ($index -lt $text.Length) {
            [void]$value.Append($text[$index])
            $index++
        }
    }

    if ($insideQuotes -or $value.Length -eq 0) {
        return [pscustomobject]@{ IsValid = $false; ExecutablePath = $null; Arguments = $null; Reason = 'INVALID_IMAGE_PATH_COMMAND_LINE' }
    }

    $arguments = if ($index -lt $text.Length) { $text.Substring($index).Trim() } else { '' }
    return [pscustomobject]@{
        IsValid = $true
        ExecutablePath = $value.ToString()
        Arguments = $arguments
        Reason = 'VALID_IMAGE_PATH_COMMAND_LINE'
    }
}

function Resolve-GaltekServiceImagePath {
    param(
        [AllowNull()][string] $CommandLine,
        [Parameter(Mandatory = $true)][string] $ExpectedExecutable
    )

    $expected = Resolve-GaltekLocalPath -Path $ExpectedExecutable
    if (-not $expected.IsValid) {
        return [pscustomobject]@{ IsValid = $false; ExecutablePath = $null; Arguments = $null; Reason = 'INVALID_EXPECTED_EXECUTABLE' }
    }

    $parsed = ConvertFrom-GaltekWindowsCommandLineExecutable -CommandLine $CommandLine
    if ($parsed.IsValid) {
        $parsedPath = Resolve-GaltekLocalPath -Path $parsed.ExecutablePath
        if ($parsedPath.IsValid -and
            [string]::Equals($parsedPath.Path, $expected.Path, [StringComparison]::OrdinalIgnoreCase)) {
            return [pscustomobject]@{ IsValid = $true; ExecutablePath = $parsedPath.Path; Arguments = [string]$parsed.Arguments; Reason = 'VALID_QUOTED_IMAGE_PATH' }
        }
    }

    # SCM/sc.exe can preserve an argument-free executable path containing spaces
    # without quotes. Treat the whole raw value as an executable only when it is
    # exactly the expected local file. This does not generalize unquoted argv.
    $text = if ($null -eq $CommandLine) { '' } else { $CommandLine.Trim() }
    if (-not $text.Contains('"')) {
        $whole = Resolve-GaltekLocalPath -Path $text
        if ($whole.IsValid -and
            [string]::Equals($whole.Path, $expected.Path, [StringComparison]::OrdinalIgnoreCase)) {
            return [pscustomobject]@{ IsValid = $true; ExecutablePath = $whole.Path; Arguments = ''; Reason = 'VALID_UNQUOTED_EXACT_IMAGE_PATH' }
        }
    }

    return [pscustomobject]@{
        IsValid = $false
        ExecutablePath = if ($parsed.IsValid) { [string]$parsed.ExecutablePath } else { $null }
        Arguments = if ($parsed.IsValid) { [string]$parsed.Arguments } else { $null }
        Reason = if ($parsed.IsValid) { 'EXECUTABLE_MISMATCH' } else { [string]$parsed.Reason }
    }
}

function Resolve-GaltekLocalPath {
    param([AllowNull()][string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return [pscustomobject]@{ IsValid = $false; Path = $null; Reason = 'MISSING_PATH' }
    }

    $candidate = $Path.Trim()
    if ($candidate.Length -ge 2 -and $candidate[0] -eq '"' -and $candidate[$candidate.Length - 1] -eq '"') {
        $candidate = $candidate.Substring(1, $candidate.Length - 2)
    }
    if ($candidate.Contains('"') -or $candidate.Contains([char]0)) {
        return [pscustomobject]@{ IsValid = $false; Path = $null; Reason = 'INVALID_PATH_SYNTAX' }
    }

    try {
        $fullPath = [IO.Path]::GetFullPath($candidate)
        $pathRoot = [IO.Path]::GetPathRoot($fullPath)
        if (-not [IO.Path]::IsPathRooted($fullPath) -or $pathRoot -notmatch '^[A-Za-z]:\\$') {
            return [pscustomobject]@{ IsValid = $false; Path = $null; Reason = 'PATH_NOT_LOCAL_DRIVE' }
        }
        if ($fullPath.Substring($pathRoot.Length).Contains(':')) {
            return [pscustomobject]@{ IsValid = $false; Path = $null; Reason = 'ALTERNATE_DATA_STREAM_NOT_ALLOWED' }
        }
        return [pscustomobject]@{ IsValid = $true; Path = $fullPath; Reason = 'VALID_LOCAL_PATH' }
    }
    catch {
        return [pscustomobject]@{ IsValid = $false; Path = $null; Reason = 'INVALID_PATH_SYNTAX' }
    }
}

function Test-GaltekPathInsideRoot {
    param(
        [AllowNull()][string] $Path,
        [Parameter(Mandatory = $true)][string] $ExpectedRoot
    )

    $resolvedPath = Resolve-GaltekLocalPath -Path $Path
    $resolvedRoot = Resolve-GaltekLocalPath -Path $ExpectedRoot
    if (-not $resolvedPath.IsValid -or -not $resolvedRoot.IsValid) { return $false }

    $fullRoot = $resolvedRoot.Path.TrimEnd('\') + '\'
    return ($resolvedPath.Path.TrimEnd('\') + '\').StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)
}

function New-GaltekLegacyAdoptionResult {
    param(
        [bool] $IsLegacyInstallation,
        [bool] $IsValid,
        [Parameter(Mandatory = $true)][string] $Reason,
        [AllowNull()][string] $Component,
        [AllowNull()][string] $Path
    )

    return [pscustomobject]@{
        IsLegacyInstallation = $IsLegacyInstallation
        IsValid = $IsValid
        Component = $Component
        Reason = $Reason
        Path = $Path
    }
}

function Test-GaltekLegacyComponentPath {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('SERVICE', 'SESSION', 'CP')][string] $Component,
        [AllowNull()][string] $Path,
        [Parameter(Mandatory = $true)][string] $ExpectedRoot,
        [Parameter(Mandatory = $true)][string] $ExpectedFileName
    )

    $resolved = Resolve-GaltekLocalPath -Path $Path
    if (-not $resolved.IsValid) {
        return New-GaltekLegacyAdoptionResult -IsLegacyInstallation $true -IsValid $false -Component $Component -Reason $resolved.Reason -Path $null
    }
    if (-not (Test-GaltekPathInsideRoot -Path $resolved.Path -ExpectedRoot $ExpectedRoot)) {
        return New-GaltekLegacyAdoptionResult -IsLegacyInstallation $true -IsValid $false -Component $Component -Reason 'PATH_OUTSIDE_GALTEK_ROOT' -Path $resolved.Path
    }
    if (-not [string]::Equals([IO.Path]::GetFileName($resolved.Path), $ExpectedFileName, [StringComparison]::OrdinalIgnoreCase)) {
        return New-GaltekLegacyAdoptionResult -IsLegacyInstallation $true -IsValid $false -Component $Component -Reason 'UNEXPECTED_EXECUTABLE_NAME' -Path $resolved.Path
    }

    return New-GaltekLegacyAdoptionResult -IsLegacyInstallation $true -IsValid $true -Component $Component -Reason 'VALID_COMPONENT_PATH' -Path $resolved.Path
}

function Test-GaltekLegacyServiceImagePath {
    param(
        [AllowNull()][string] $CommandLine,
        [Parameter(Mandatory = $true)][string] $ExpectedRoot
    )

    $expectedExecutable = Join-Path $ExpectedRoot 'GaltekClassroom.Agent.Service.exe'
    $semantic = Resolve-GaltekServiceImagePath -CommandLine $CommandLine -ExpectedExecutable $expectedExecutable
    if ($semantic.IsValid) {
        return Test-GaltekLegacyComponentPath -Component 'SERVICE' -Path $semantic.ExecutablePath -ExpectedRoot $ExpectedRoot -ExpectedFileName 'GaltekClassroom.Agent.Service.exe'
    }

    $parsed = ConvertFrom-GaltekWindowsCommandLineExecutable -CommandLine $CommandLine
    if (-not $parsed.IsValid) {
        return New-GaltekLegacyAdoptionResult -IsLegacyInstallation $true -IsValid $false -Component 'SERVICE' -Reason $parsed.Reason -Path $null
    }

    $parsedResult = Test-GaltekLegacyComponentPath `
        -Component 'SERVICE' `
        -Path $parsed.ExecutablePath `
        -ExpectedRoot $ExpectedRoot `
        -ExpectedFileName 'GaltekClassroom.Agent.Service.exe'
    if ($parsedResult.IsValid) { return $parsedResult }

    # Some legacy installs stored the exact Service executable path without
    # quotes even though Program Files contains spaces. Windows token parsing
    # necessarily sees C:\Program as argv[0]. Adopt only when the complete,
    # argument-free value itself normalizes to the one expected Galtek Service
    # executable. Values containing quotes or appended arguments stay rejected.
    $text = if ($null -eq $CommandLine) { '' } else { $CommandLine.Trim() }
    if (-not $text.Contains('"')) {
        $wholeValueResult = Test-GaltekLegacyComponentPath `
            -Component 'SERVICE' `
            -Path $text `
            -ExpectedRoot $ExpectedRoot `
            -ExpectedFileName 'GaltekClassroom.Agent.Service.exe'
        if ($wholeValueResult.IsValid) { return $wholeValueResult }
    }

    return $parsedResult
}

function Get-GaltekLegacyAdoptionPlan {
    param(
        [Parameter(Mandatory = $true)] $State,
        [Parameter(Mandatory = $true)][string] $InstallRoot
    )

    $hasLifecycleResource = [bool]$State.ServiceExists -or [bool]$State.LegacyServiceExists -or
        [bool]$State.SessionTaskExists -or [bool]$State.CredentialProviderRegistered

    if (-not $hasLifecycleResource) {
        return New-GaltekLegacyAdoptionResult -IsLegacyInstallation $false -IsValid $true -Component $null -Reason 'FRESH_INSTALL' -Path $null
    }

    $root = Resolve-GaltekLocalPath -Path $InstallRoot
    if (-not $root.IsValid) {
        return New-GaltekLegacyAdoptionResult -IsLegacyInstallation $true -IsValid $false -Component 'INSTALL_ROOT' -Reason $root.Reason -Path $null
    }

    if ([bool]$State.ServiceExists -or [bool]$State.LegacyServiceExists) {
        $serviceResult = Test-GaltekLegacyServiceImagePath -CommandLine ([string]$State.ServiceImagePath) -ExpectedRoot $root.Path
        if (-not $serviceResult.IsValid) { return $serviceResult }
    }

    if ([bool]$State.SessionTaskExists) {
        $sessionRoot = Join-Path $root.Path 'Session'
        $sessionResult = Test-GaltekLegacyComponentPath -Component 'SESSION' -Path ([string]$State.SessionExecutablePath) -ExpectedRoot $sessionRoot -ExpectedFileName 'GaltekClassroom.Agent.Session.exe'
        if (-not $sessionResult.IsValid) { return $sessionResult }
    }

    if ([bool]$State.CredentialProviderRegistered) {
        $credentialRoot = Join-Path $root.Path 'CredentialProvider'
        $credentialResult = Test-GaltekLegacyComponentPath -Component 'CP' -Path ([string]$State.CredentialProviderPath) -ExpectedRoot $credentialRoot -ExpectedFileName 'GaltekClassroom.CredentialProvider.dll'
        if (-not $credentialResult.IsValid) { return $credentialResult }
    }

    return New-GaltekLegacyAdoptionResult -IsLegacyInstallation $true -IsValid $true -Component $null -Reason 'VALID_LEGACY_INSTALLATION' -Path $null
}
