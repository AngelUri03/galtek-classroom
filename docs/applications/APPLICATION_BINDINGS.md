# Application bindings locales

Prompt 17A agrega la fuente de verdad local del Client para vincular un `applicationId` logico Galtek con una aplicacion abrible en ese equipo. No lanza aplicaciones todavia.

## Separacion de conceptos

`ApplicationDefinition` vive en el Master y describe que aplicaciones conoce o autoriza el aula.

`ApplicationBinding` vive en el Client y describe como ese equipo encuentra localmente una aplicacion por `applicationId`.

El Master solo puede enviar `applicationId` en una fase futura de `OPEN_APPLICATION`. Nunca debe enviar `executablePath`, command line, argumentos, working directory, shell, PowerShell, `cmd`, scripts, shortcuts, MSI ni URI arbitraria.

## Archivo

El catalogo local se guarda en:

```text
<CommonApplicationData>\Galtek\Classroom\application-bindings.json
```

Permite override de desarrollo mediante `GALTEK_CLASSROOM_DATA_DIR`, igual que el resto de datos persistentes del Agent.

El archivo esta separado de `installation.json`, `license.dat`, `master-binding.json`, `network-identity.json`, `authorized-masters.json` y los estados/journals de browser policy.

## Formato

```json
{
  "schemaVersion": 1,
  "bindings": [
    {
      "applicationId": "microsoft-word",
      "launchType": "APP_PATHS",
      "appPathExecutableName": "WINWORD.EXE",
      "executablePath": null,
      "enabled": true,
      "createdAtUtc": "2026-09-03T12:00:00+00:00",
      "updatedAtUtc": "2026-09-03T12:00:00+00:00"
    }
  ]
}
```

## Launch types

Soportados en 17A:

- `APP_PATHS`: nombre de ejecutable `.exe` registrable/localizable por Windows App Paths, sin ruta ni argumentos.
- `ABSOLUTE_EXE`: ruta local absoluta a un `.exe`, configurada por administrador local.

No soportados en 17A: MSIX, UWP AUMID, URI/protocol, shortcut/LNK, BAT/CMD/PS1/VBS/JS, MSI, documento, shell verb, store app ni command line custom.

## Validacion

`APP_PATHS` exige nombre de archivo simple `.exe`, sin `\`, `/`, `:`, comillas, espacios, control chars, path ni argumentos.

`ABSOLUTE_EXE` exige path absoluto Windows local, `.exe`, no UNC, no relativo, sin `..`, ADS, control chars, comillas, argumentos, wildcards ni placeholders de variables de entorno. Al crear o reemplazar un binding `ABSOLUTE_EXE`, el archivo debe existir en ese momento.

`applicationId` no se deriva del filename ni del display name. Se valida como identificador estable no vacio con letras, numeros, punto, guion bajo y guion.

## Corrupcion

JSON invalido, schema desconocido, duplicados, launch type desconocido o campos incompatibles producen `APPLICATION_BINDINGS_INVALID`. El Agent preserva el archivo y no adopta parcialmente entradas buenas.

## CLI local

Comandos administrativos:

```text
--application-bind-list
--application-bind-exe <applicationId> <absoluteExePath>
--application-bind-app-path <applicationId> <executableName>
--application-bind-disable <applicationId>
--application-bind-enable <applicationId>
--application-bind-remove <applicationId>
--replace-application-binding
```

Las mutaciones requieren consola elevada. `--application-bind-list` es read-only y no crea ni modifica el archivo.

## Performance

El catalogo no agrega timers, polling, discovery, registry scan, WMI, process scan, filesystem scan, heartbeat data ni writes en idle. Solo hay I/O cuando se ejecuta la CLI o cuando una fase futura consulte explicitamente el catalogo.
