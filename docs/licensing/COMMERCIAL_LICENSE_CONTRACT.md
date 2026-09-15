# Galtek Classroom Commercial License Contract

This document records the current consumer-side contract enforced by
`GaltekClassroom.Agent.Service`.

## Authority

Classroom does not generate Commercial Licenses. Galtek Hub is the issuer. The
Classroom Agent only validates a received JWT and persists `license.dat` after
the candidate token has validated successfully.

Authority files:

- `agent/src/GaltekClassroom.Agent.Service/Licensing/CommercialLicenseValidator.cs`
- `agent/src/GaltekClassroom.Agent.Service/Licensing/CommercialLicenseManager.cs`
- `agent/src/GaltekClassroom.Agent.Service/Licensing/CommercialLicenseStore.cs`
- `agent/src/GaltekClassroom.Agent.Service/Licensing/LicensePublicKeyProvider.cs`
- `agent/src/GaltekClassroom.Agent.Service/Identity/MachineCodeGenerator.cs`
- `agent/src/GaltekClassroom.Agent.Shared/MachineCodePayload.cs`

## Machine Code

`GaltekClassroom.Agent.Service.exe --machine-code` prints Base64 of compact
UTF-8 JSON:

```json
{
  "product": "GALTEK_CLASSROOM",
  "schemaVersion": 1,
  "installationId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "cpuHash": "<lowercase sha256 hex>",
  "motherboardHash": "<lowercase sha256 hex>",
  "macHash": "<lowercase sha256 hex>",
  "diskHash": "<lowercase sha256 hex>",
  "hostname": "PC14"
}
```

The Machine Code exists before activation. Hub must decode it without changing
`installationId` or the hardware hashes.

## JWT

Classroom accepts only signed JWTs using `RS256`.

Required claims:

- `iss`: exact string `galtek-hub`.
- `aud`: exact string `galtek-classroom`, or a single-item audience array with
  that value.
- `sub`: installation UUID; must equal local Installation Identity
  `installationId`.
- `jti`: non-empty license id string.
- `product`: exact string `GALTEK_CLASSROOM`.
- `schemaVersion`: integer `1`.
- `cpuHash`, `motherboardHash`, `macHash`, `diskHash`: lowercase SHA-256 hex.
- `roles`: array of strings. Known roles are `CLIENT` and `MASTER`; unknown
  roles are ignored. The Agent requires `CLIENT`.
- `iat`: NumericDate.
- `exp`: NumericDate, required and later than `iat`.

Optional claims:

- `organizationId`: non-empty string when present.
- `features`: object when present. Feature names are extensible. Current typed
  readers include `screenMonitoring`, `screenProjection`, `inputLock`,
  `remoteAppLaunch`, `remoteShutdown`, `multiMaster`, and
  `maxManagedClients`.
- `nbf`: not required; if present, normal lifetime validation rejects future
  values.

## Hardware Tolerance

Classroom compares the JWT hardware hashes against current hardware and accepts
the license when at least 3 of 4 components match.

## Public Key

For development, set:

```powershell
$env:GALTEK_CLASSROOM_LICENSE_PUBLIC_KEY_PATH = "C:\path\to\galtek-classroom-public-key.pem"
```

The configured file must contain a public key only. Classroom rejects private
key material in this path. There is no dynamic key download or automatic trust
from the network.

## Activation

Activation validates the candidate token before writing `license.dat`.

```powershell
GaltekClassroom.Agent.Service.exe --activate-license-file .\dev-license.jwt
GaltekClassroom.Agent.Service.exe --license-status
```

An invalid candidate must not replace an already valid installed license.
