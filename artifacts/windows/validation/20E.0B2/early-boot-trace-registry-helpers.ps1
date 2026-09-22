#Requires -Version 5.1
Set-StrictMode -Version Latest

function Get-ServiceEnvironmentValue {
    param([Parameter(Mandatory = $true)][Microsoft.Win32.RegistryKey] $Key)

    if ($Key.GetValueNames() -notcontains 'Environment') {
        return [pscustomobject]@{ Exists = $false; Values = @() }
    }
    if ($Key.GetValueKind('Environment') -ne [Microsoft.Win32.RegistryValueKind]::MultiString) {
        throw 'Service Environment is not REG_MULTI_SZ; no registry change was made.'
    }

    $values = @($Key.GetValue(
        'Environment',
        $null,
        [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames))
    return [pscustomobject]@{ Exists = $true; Values = $values }
}

function Restore-ServiceEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)][Microsoft.Win32.RegistryKey] $WritableServiceKey,
        [Parameter(Mandatory = $true)][bool] $PreviousExisted,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]] $PreviousEnvironment
    )

    $current = Get-ServiceEnvironmentValue -Key $WritableServiceKey
    $reservedNames = @('COREHOST_TRACE', 'COREHOST_TRACEFILE', 'COREHOST_TRACE_VERBOSITY')
    foreach ($entry in $current.Values) {
        $separator = $entry.IndexOf('=')
        $name = if ($separator -gt 0) { $entry.Substring(0, $separator) } else { '' }
        if ($reservedNames -notcontains $name) {
            throw 'Service Environment now contains a non-F2 entry; no registry change was made.'
        }
    }

    if ($PreviousExisted) {
        $WritableServiceKey.SetValue(
            'Environment',
            [string[]]$PreviousEnvironment,
            [Microsoft.Win32.RegistryValueKind]::MultiString)
    }
    elseif ($current.Exists) {
        $WritableServiceKey.DeleteValue('Environment', $false)
    }
}

function Get-ServiceEnvironmentStateHash {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]] $Values)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $payload = [Text.Encoding]::UTF8.GetBytes([string]::Join([char]0, $Values))
        return ([BitConverter]::ToString($sha256.ComputeHash($payload))).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}
