Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:GaltekCredentialProviderClsid = '{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}'
$script:GaltekCredentialProviderName = 'Galtek Classroom Credential Provider'
$script:GaltekCredentialProviderFileName = 'GaltekClassroom.CredentialProvider.dll'
$script:GaltekCredentialProviderManifestName = 'credential-provider.manifest.json'
$script:GaltekCredentialProviderArchitecture = 'x64'
$script:GaltekCredentialProviderProduct = 'GALTEK_CLASSROOM'
$script:GaltekCredentialProviderComponent = 'CREDENTIAL_PROVIDER'
$script:GaltekCredentialProviderPackageSchemaVersion = 1
$script:GaltekAgentServiceName = 'GaltekClassroomAgent'
$script:GaltekCredentialProviderRegistrySubKey = "SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\$script:GaltekCredentialProviderClsid"
$script:GaltekCredentialProviderFilterRegistrySubKey = "SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Provider Filters\$script:GaltekCredentialProviderClsid"
$script:GaltekCredentialProviderClsidRegistrySubKey = "SOFTWARE\Classes\CLSID\$script:GaltekCredentialProviderClsid"
$script:GaltekCredentialProviderInprocRegistrySubKey = "$script:GaltekCredentialProviderClsidRegistrySubKey\InprocServer32"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-IsWindowsX64 {
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw 'Credential Provider lifecycle requires Windows x64.'
    }
}

function Assert-IsProcessX64 {
    if (-not [Environment]::Is64BitProcess) {
        throw 'Credential Provider lifecycle requires 64-bit PowerShell. Re-run from %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe.'
    }
}

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)

    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-IsElevated {
    if (-not (Test-IsElevated)) {
        throw 'This Credential Provider lifecycle command must be run from an elevated PowerShell session.'
    }
}

function Assert-PathInsideDirectory {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $ParentDirectory,
        [Parameter(Mandatory = $true)][string] $Purpose
    )

    $fullPath = Resolve-FullPath $Path
    $fullParent = (Resolve-FullPath $ParentDirectory).TrimEnd('\') + '\'
    $fullPathWithSeparator = $fullPath.TrimEnd('\') + '\'

    if (-not $fullPathWithSeparator.StartsWith($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose must stay under $fullParent. Actual path: $fullPath"
    }
}

function Assert-SafePublishPath {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $RepositoryRoot,
        [Parameter(Mandatory = $true)][bool] $AllowOutsideArtifacts
    )

    $fullPath = Resolve-FullPath $Path
    $artifactsRoot = Resolve-FullPath (Join-Path $RepositoryRoot 'artifacts')
    $artifactsRootWithSeparator = $artifactsRoot.TrimEnd('\') + '\'
    $pathWithSeparator = $fullPath.TrimEnd('\') + '\'
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)

    if ($fullPath -eq $pathRoot) {
        throw "Refusing to clean publish output at filesystem root: $fullPath"
    }

    if (-not $pathWithSeparator.StartsWith($artifactsRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase) -and -not $AllowOutsideArtifacts) {
        throw "Default publish output must stay under artifacts/: $fullPath"
    }
}

function Get-CredentialProviderInstallRoot {
    $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    return Resolve-FullPath (Join-Path $programFiles 'Galtek\Classroom\Agent\CredentialProvider')
}

function Get-CredentialProviderVersionsRoot {
    return Resolve-FullPath (Join-Path (Get-CredentialProviderInstallRoot) 'versions')
}

function Assert-PathInsideCredentialProviderRoot {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Purpose
    )

    Assert-PathInsideDirectory -Path $Path -ParentDirectory (Get-CredentialProviderInstallRoot) -Purpose $Purpose
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-PeX64 {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        if ($stream.Length -lt 0x40) {
            throw "File is too small to be a PE image: $Path"
        }

        $reader = [System.IO.BinaryReader]::new($stream)
        try {
            if ($reader.ReadUInt16() -ne 0x5A4D) {
                throw "File does not have an MZ header: $Path"
            }

            $stream.Seek(0x3C, [System.IO.SeekOrigin]::Begin) | Out-Null
            $peOffset = $reader.ReadInt32()
            if ($peOffset -lt 0 -or ($peOffset + 6) -gt $stream.Length) {
                throw "File has an invalid PE header offset: $Path"
            }

            $stream.Seek($peOffset, [System.IO.SeekOrigin]::Begin) | Out-Null
            if ($reader.ReadUInt32() -ne 0x00004550) {
                throw "File does not have a PE signature: $Path"
            }

            $machine = $reader.ReadUInt16()
            if ($machine -ne 0x8664) {
                throw "Credential Provider DLL must be PE x64 (AMD64). Actual machine: 0x$($machine.ToString('X4'))"
            }
        }
        finally {
            if ($null -ne $reader) {
                $reader.Dispose()
            }
        }
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

function Get-AuthenticodeDiagnostic {
    param([Parameter(Mandatory = $true)][string] $Path)

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $status = $signature.Status.ToString()
    $diagnostic = if ($status -eq 'Valid') {
        'SIGNED_VALID'
    }
    elseif ($status -eq 'NotSigned') {
        'NOT_SIGNED'
    }
    else {
        'SIGNED_INVALID'
    }

    return [pscustomobject]@{
        Diagnostic = $diagnostic
        Status = $status
        StatusMessage = $signature.StatusMessage
    }
}

function Assert-AuthenticodeAcceptable {
    param([Parameter(Mandatory = $true)][string] $Path)

    $diagnostic = Get-AuthenticodeDiagnostic -Path $Path
    if ($diagnostic.Diagnostic -eq 'SIGNED_INVALID') {
        throw "SIGNED_INVALID: Authenticode signature status is $($diagnostic.Status). $($diagnostic.StatusMessage)"
    }

    return $diagnostic
}

function Resolve-CredentialProviderPackageDirectory {
    param([Parameter(Mandatory = $true)][string] $PackagePath)

    $resolved = Resolve-FullPath $PackagePath
    if ((Test-Path -LiteralPath $resolved -PathType Leaf) -and ([System.IO.Path]::GetFileName($resolved) -eq $script:GaltekCredentialProviderManifestName)) {
        return [System.IO.Path]::GetDirectoryName($resolved)
    }

    return $resolved
}

function Test-PathSegmentSafe {
    param([Parameter(Mandatory = $true)][string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    return $Value.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -lt 0
}

function Test-FileSystemRightsIncludeWrite {
    param([Parameter(Mandatory = $true)][System.Security.AccessControl.FileSystemRights] $Rights)

    $writeMask =
        [System.Security.AccessControl.FileSystemRights]::WriteData -bor
        [System.Security.AccessControl.FileSystemRights]::CreateFiles -bor
        [System.Security.AccessControl.FileSystemRights]::AppendData -bor
        [System.Security.AccessControl.FileSystemRights]::CreateDirectories -bor
        [System.Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor
        [System.Security.AccessControl.FileSystemRights]::WriteAttributes -bor
        [System.Security.AccessControl.FileSystemRights]::Delete -bor
        [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [System.Security.AccessControl.FileSystemRights]::ChangePermissions -bor
        [System.Security.AccessControl.FileSystemRights]::TakeOwnership

    return (($Rights -band $writeMask) -ne 0)
}

function Get-StandardUserSidValues {
    return @(
        'S-1-1-0',
        'S-1-5-11',
        'S-1-5-32-545'
    )
}

function Resolve-IdentityReferenceSidValue {
    param([Parameter(Mandatory = $true)][System.Security.Principal.IdentityReference] $IdentityReference)

    try {
        return $IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
    }
    catch {
        return $IdentityReference.Value
    }
}

function Test-FileSystemAccessRuleGrantsStandardUserWrite {
    param(
        [Parameter(Mandatory = $true)] $Rule,
        [string[]] $StandardUserSids = (Get-StandardUserSidValues)
    )

    if ($Rule.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow) {
        return $false
    }

    $sid = Resolve-IdentityReferenceSidValue -IdentityReference $Rule.IdentityReference
    return ($StandardUserSids -contains $sid -and (Test-FileSystemRightsIncludeWrite -Rights $Rule.FileSystemRights))
}

function Get-StandardUserWritableAclEntries {
    param([Parameter(Mandatory = $true)][string] $Path)

    $standardUserSids = Get-StandardUserSidValues

    $acl = Get-Acl -LiteralPath $Path
    foreach ($rule in $acl.Access) {
        if (Test-FileSystemAccessRuleGrantsStandardUserWrite -Rule $rule -StandardUserSids $standardUserSids) {
            $sid = Resolve-IdentityReferenceSidValue -IdentityReference $rule.IdentityReference
            [pscustomobject]@{
                Path = $Path
                Identity = $rule.IdentityReference.Value
                Sid = $sid
                Rights = $rule.FileSystemRights.ToString()
                IsInherited = $rule.IsInherited
            }
        }
    }
}

function Assert-NoStandardUserWriteAccess {
    param([Parameter(Mandatory = $true)][string[]] $Paths)

    $badEntries = @()
    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "ACL target does not exist: $path"
        }

        $badEntries += @(Get-StandardUserWritableAclEntries -Path $path)
    }

    if ($badEntries.Count -gt 0) {
        $summary = ($badEntries | ForEach-Object { "$($_.Path) [$($_.Identity): $($_.Rights), inherited=$($_.IsInherited)]" }) -join '; '
        throw "Credential Provider binaries must not be writable by standard users. Writable ACL entries: $summary"
    }
}

function Test-StringProperty {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Expected
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or [string] $property.Value -ne $Expected) {
        throw "Manifest field '$Name' must be '$Expected'."
    }
}

function Assert-CredentialProviderPackage {
    param(
        [Parameter(Mandatory = $true)][string] $PackagePath,
        [switch] $RequireCleanPackage
    )

    $packageDirectory = Resolve-CredentialProviderPackageDirectory -PackagePath $PackagePath
    if (-not (Test-Path -LiteralPath $packageDirectory -PathType Container)) {
        throw "Credential Provider package directory was not found: $packageDirectory"
    }

    $manifestPath = Join-Path $packageDirectory $script:GaltekCredentialProviderManifestName
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Credential Provider manifest was not found: $manifestPath"
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

    if ([int] $manifest.schemaVersion -ne $script:GaltekCredentialProviderPackageSchemaVersion) {
        throw "Manifest schemaVersion must be $script:GaltekCredentialProviderPackageSchemaVersion."
    }

    Test-StringProperty -Object $manifest -Name 'product' -Expected $script:GaltekCredentialProviderProduct
    Test-StringProperty -Object $manifest -Name 'component' -Expected $script:GaltekCredentialProviderComponent
    Test-StringProperty -Object $manifest -Name 'clsid' -Expected $script:GaltekCredentialProviderClsid
    Test-StringProperty -Object $manifest -Name 'architecture' -Expected $script:GaltekCredentialProviderArchitecture
    Test-StringProperty -Object $manifest -Name 'fileName' -Expected $script:GaltekCredentialProviderFileName

    if (-not (Test-PathSegmentSafe -Value ([string] $manifest.packageId))) {
        throw 'Manifest packageId must be a safe non-empty path segment.'
    }

    $dllPath = Join-Path $packageDirectory $script:GaltekCredentialProviderFileName
    if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
        throw "Credential Provider DLL was not found: $dllPath"
    }

    Assert-PeX64 -Path $dllPath

    $sha256 = Get-FileSha256 -Path $dllPath
    if ([string] $manifest.sha256 -ne $sha256) {
        throw "Credential Provider SHA-256 mismatch. Manifest: $($manifest.sha256). Actual: $sha256."
    }

    $expectedPackageId = "sha256-$($sha256.Substring(0, 16))"
    if ([string] $manifest.packageId -ne $expectedPackageId) {
        throw "Manifest packageId must be deterministic for this DLL. Expected: $expectedPackageId. Actual: $($manifest.packageId)."
    }

    $signature = Assert-AuthenticodeAcceptable -Path $dllPath

    if ($RequireCleanPackage) {
        $allowed = @($script:GaltekCredentialProviderFileName, $script:GaltekCredentialProviderManifestName)
        $unexpected = @(Get-ChildItem -LiteralPath $packageDirectory -Force | Where-Object { $allowed -notcontains $_.Name })
        if ($unexpected.Count -gt 0) {
            throw "Credential Provider package contains unexpected files: $(($unexpected | ForEach-Object { $_.Name }) -join ', ')"
        }
    }

    return [pscustomobject]@{
        PackageDirectory = $packageDirectory
        ManifestPath = $manifestPath
        DllPath = $dllPath
        Manifest = $manifest
        Sha256 = $sha256
        SignatureDiagnostic = $signature.Diagnostic
        SignatureStatus = $signature.Status
    }
}

function Get-Hklm64Root {
    return [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        [Microsoft.Win32.RegistryView]::Registry64)
}

function Test-Hklm64SubKey {
    param([Parameter(Mandatory = $true)][string] $SubKey)

    $root = Get-Hklm64Root
    try {
        $key = $root.OpenSubKey($SubKey, $false)
        try {
            return $null -ne $key
        }
        finally {
            if ($null -ne $key) {
                $key.Dispose()
            }
        }
    }
    finally {
        $root.Dispose()
    }
}

function Get-Hklm64StringValue {
    param(
        [Parameter(Mandatory = $true)][string] $SubKey,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Name
    )

    $root = Get-Hklm64Root
    try {
        $key = $root.OpenSubKey($SubKey, $false)
        try {
            if ($null -eq $key) {
                return $null
            }

            return $key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
        finally {
            if ($null -ne $key) {
                $key.Dispose()
            }
        }
    }
    finally {
        $root.Dispose()
    }
}

function Set-Hklm64StringValue {
    param(
        [Parameter(Mandatory = $true)][string] $SubKey,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Name,
        [Parameter(Mandatory = $true)][string] $Value
    )

    $root = Get-Hklm64Root
    try {
        $key = $root.CreateSubKey($SubKey)
        try {
            $key.SetValue($Name, $Value, [Microsoft.Win32.RegistryValueKind]::String)
        }
        finally {
            $key.Dispose()
        }
    }
    finally {
        $root.Dispose()
    }
}

function Remove-Hklm64SubKeyTree {
    param([Parameter(Mandatory = $true)][string] $SubKey)

    $root = Get-Hklm64Root
    try {
        try {
            $root.DeleteSubKeyTree($SubKey, $false)
        }
        catch [System.ArgumentException] {
        }
    }
    finally {
        $root.Dispose()
    }
}
