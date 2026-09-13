#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'credential-provider-common.ps1')

function Assert-Equal {
    param(
        [AllowNull()] $Expected,
        [AllowNull()] $Actual,
        [Parameter(Mandatory = $true)][string] $Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected: [$Expected]. Actual: [$Actual]."
    }
}

function Assert-TextDoesNotContain {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $ForbiddenText,
        [Parameter(Mandatory = $true)][string] $Message
    )

    if ($Text.Contains($ForbiddenText)) {
        throw "$Message Forbidden text found: [$ForbiddenText]."
    }
}

function New-TestFileSystemAccessRule {
    param(
        [Parameter(Mandatory = $true)][string] $Sid,
        [Parameter(Mandatory = $true)][System.Security.AccessControl.FileSystemRights] $Rights,
        [System.Security.AccessControl.AccessControlType] $AccessControlType = [System.Security.AccessControl.AccessControlType]::Allow
    )

    $identity = [System.Security.Principal.SecurityIdentifier]::new($Sid)
    return [System.Security.AccessControl.FileSystemAccessRule]::new($identity, $Rights, $AccessControlType)
}

function Assert-StandardUserAllowIsWritable {
    param(
        [Parameter(Mandatory = $true)][System.Security.AccessControl.FileSystemRights] $Rights,
        [Parameter(Mandatory = $true)][string] $Message
    )

    $rule = New-TestFileSystemAccessRule -Sid 'S-1-5-32-545' -Rights $Rights
    Assert-Equal -Expected $true -Actual (Test-FileSystemRightsIncludeWrite -Rights $Rights) -Message "$Message Rights mask should be classified as writable."
    Assert-Equal -Expected $true -Actual (Test-FileSystemAccessRuleGrantsStandardUserWrite -Rule $rule) -Message "$Message Allow ACE should be classified as standard-user writable."
}

function Assert-StandardUserAllowIsReadOnly {
    param(
        [Parameter(Mandatory = $true)][System.Security.AccessControl.FileSystemRights] $Rights,
        [Parameter(Mandatory = $true)][string] $Message
    )

    $rule = New-TestFileSystemAccessRule -Sid 'S-1-5-32-545' -Rights $Rights
    Assert-Equal -Expected $false -Actual (Test-FileSystemRightsIncludeWrite -Rights $Rights) -Message "$Message Rights mask should be classified as read-only."
    Assert-Equal -Expected $false -Actual (Test-FileSystemAccessRuleGrantsStandardUserWrite -Rule $rule) -Message "$Message Allow ACE should not be classified as standard-user writable."
}

$readAndExecuteWithSynchronize =
    [System.Security.AccessControl.FileSystemRights]::ReadAndExecute -bor
    [System.Security.AccessControl.FileSystemRights]::Synchronize

$executeReadPermissionsWithSynchronize =
    [System.Security.AccessControl.FileSystemRights]::ExecuteFile -bor
    [System.Security.AccessControl.FileSystemRights]::ReadPermissions -bor
    [System.Security.AccessControl.FileSystemRights]::Synchronize

Assert-StandardUserAllowIsReadOnly -Rights $readAndExecuteWithSynchronize -Message 'ReadAndExecute plus Synchronize'
Assert-StandardUserAllowIsReadOnly -Rights ([System.Security.AccessControl.FileSystemRights]::Read) -Message 'Read'
Assert-StandardUserAllowIsReadOnly -Rights $executeReadPermissionsWithSynchronize -Message 'ExecuteFile plus ReadPermissions plus Synchronize'

Assert-StandardUserAllowIsWritable -Rights ([System.Security.AccessControl.FileSystemRights]::WriteData) -Message 'WriteData'
Assert-StandardUserAllowIsWritable -Rights ([System.Security.AccessControl.FileSystemRights]::AppendData) -Message 'AppendData'
Assert-StandardUserAllowIsWritable -Rights ([System.Security.AccessControl.FileSystemRights]::WriteAttributes) -Message 'WriteAttributes'
Assert-StandardUserAllowIsWritable -Rights ([System.Security.AccessControl.FileSystemRights]::WriteExtendedAttributes) -Message 'WriteExtendedAttributes'
Assert-StandardUserAllowIsWritable -Rights ([System.Security.AccessControl.FileSystemRights]::Delete) -Message 'Delete'
Assert-StandardUserAllowIsWritable -Rights ([System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles) -Message 'DeleteSubdirectoriesAndFiles'
Assert-StandardUserAllowIsWritable -Rights ([System.Security.AccessControl.FileSystemRights]::ChangePermissions) -Message 'ChangePermissions'
Assert-StandardUserAllowIsWritable -Rights ([System.Security.AccessControl.FileSystemRights]::TakeOwnership) -Message 'TakeOwnership'
Assert-StandardUserAllowIsWritable -Rights ([System.Security.AccessControl.FileSystemRights]::Write) -Message 'Write'
Assert-StandardUserAllowIsWritable -Rights ([System.Security.AccessControl.FileSystemRights]::Modify) -Message 'Modify'
Assert-StandardUserAllowIsWritable -Rights ([System.Security.AccessControl.FileSystemRights]::FullControl) -Message 'FullControl'

$standardUserDenyWrite = New-TestFileSystemAccessRule `
    -Sid 'S-1-5-32-545' `
    -Rights ([System.Security.AccessControl.FileSystemRights]::Write) `
    -AccessControlType ([System.Security.AccessControl.AccessControlType]::Deny)
Assert-Equal -Expected $false -Actual (Test-FileSystemAccessRuleGrantsStandardUserWrite -Rule $standardUserDenyWrite) -Message 'Deny ACE with Write must not be treated as a writable grant.'

$standardUserDenyModify = New-TestFileSystemAccessRule `
    -Sid 'S-1-5-32-545' `
    -Rights ([System.Security.AccessControl.FileSystemRights]::Modify) `
    -AccessControlType ([System.Security.AccessControl.AccessControlType]::Deny)
Assert-Equal -Expected $false -Actual (Test-FileSystemAccessRuleGrantsStandardUserWrite -Rule $standardUserDenyModify) -Message 'Deny ACE with Modify must not be treated as a writable grant.'

$administratorsFullControl = New-TestFileSystemAccessRule `
    -Sid 'S-1-5-32-544' `
    -Rights ([System.Security.AccessControl.FileSystemRights]::FullControl)
Assert-Equal -Expected $false -Actual (Test-FileSystemAccessRuleGrantsStandardUserWrite -Rule $administratorsFullControl) -Message 'Administrators FullControl must not be reported as standard-user writable.'

$standardUserSids = Get-StandardUserSidValues
Assert-Equal -Expected $true -Actual ($standardUserSids -contains 'S-1-1-0') -Message 'Everyone SID must be checked by SID value.'
Assert-Equal -Expected $true -Actual ($standardUserSids -contains 'S-1-5-11') -Message 'Authenticated Users SID must be checked by SID value.'
Assert-Equal -Expected $true -Actual ($standardUserSids -contains 'S-1-5-32-545') -Message 'Builtin Users SID must be checked by SID value.'

$commonSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'credential-provider-common.ps1') -Raw
Assert-TextDoesNotContain -Text $commonSource -ForbiddenText 'BUILTIN\Users' -Message 'ACL helper must not depend on English group names.'
Assert-TextDoesNotContain -Text $commonSource -ForbiddenText 'BUILTIN\Usuarios' -Message 'ACL helper must not depend on Spanish group names.'
Assert-TextDoesNotContain -Text $commonSource -ForbiddenText 'Everyone' -Message 'ACL helper must not depend on English Everyone name.'
Assert-TextDoesNotContain -Text $commonSource -ForbiddenText 'Todos' -Message 'ACL helper must not depend on Spanish Everyone name.'

Write-Host 'Credential Provider ACL contract tests passed.'
