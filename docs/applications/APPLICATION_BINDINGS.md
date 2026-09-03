# Application bindings locales

Prompt 17A agrega la fuente de verdad local del Client para vincular un `applicationId` logico Galtek con una aplicacion abrible en ese equipo. Prompt 17B implementa `OPEN_APPLICATION(applicationId)` productivo del lado Agent: el Master/Protobuf y el Session Command siguen transportando solo `applicationId`; el Service y el Session Agent resuelven localmente el binding antes de lanzar.

## Separacion de conceptos

`ApplicationDefinition` vive en el Master y describe que aplicaciones conoce o autoriza el aula.

`ApplicationBinding` vive en el Client y describe como ese equipo encuentra localmente una aplicacion por `applicationId`.

El Master solo puede enviar `applicationId` para `OPEN_APPLICATION`. Nunca debe enviar `executablePath`, command line, argumentos, working directory, shell, PowerShell, `cmd`, scripts, shortcuts, MSI ni URI arbitraria.

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

## OPEN_APPLICATION productivo

`OPEN_APPLICATION` llega como `OperationRequest` tipado con `OpenApplicationOperationParameters.applicationId`. El Agent Service valida el `applicationId`, carga `application-bindings.json`, exige catalogo valido, binding existente y `enabled = true`, valida la estructura local y envia al Session Agent un Session Command `OPEN_APPLICATION` que tambien contiene solo `applicationId`.

El Agent Service no crea procesos, no resuelve HKCU, no usa shell, no pasa rutas al Session Agent y no envia argumentos. La licencia comercial, pairing, Device registrado y dedupe siguen siendo responsabilidad del dispatcher remoto antes del handler.

El Session Agent vuelve a leer el catalogo on-demand, vuelve a validar schema/binding/enabled/target y resuelve el target fisico. Si el catalogo falta o el `applicationId` no existe devuelve `APPLICATION_BINDING_NOT_FOUND`; si el JSON/schema es invalido devuelve `APPLICATION_BINDINGS_INVALID`; si el binding esta disabled devuelve `APPLICATION_DISABLED` sin resolver ni comprobar ejecutable.

Para `ABSOLUTE_EXE`, el Session Agent revalida ruta absoluta Windows local `.exe`, no UNC, no relativa, sin `..`, ADS, control chars, comillas, argumentos, wildcards ni placeholders, y ejecuta `File.Exists` justo antes de lanzar. Si falta devuelve `APPLICATION_EXECUTABLE_NOT_FOUND`.

Para `APP_PATHS`, el Session Agent resuelve explicitamente solo el valor default de `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\<executableName>` en Registry64 y Registry32 cuando corresponde. No consulta HKCU, PATH, Program Files, Start Menu, WindowsApps, uninstall keys, procesos ni discos. Si ambas vistas existen y apuntan a rutas distintas falla cerrado con `APPLICATION_BINDING_INVALID`; si no resuelve o el archivo no existe devuelve `APPLICATION_EXECUTABLE_NOT_FOUND`.

El launch productivo usa `CreateProcessW` con `lpApplicationName` igual a la ruta absoluta resuelta, `lpCommandLine = null`, `bInheritHandles = false` y working directory derivado del directorio padre del `.exe`. El proceso hereda usuario/sesion/privilegios normales del Session Agent; no hay `runas`, UAC intencional, `cmd`, PowerShell, ShellExecute, argumentos, monitoring, polling ni `WaitForExit`. `SUCCESS` significa solo que Windows acepto crear el proceso.

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

El catalogo no agrega timers, polling, discovery, registry scan, WMI, process scan, filesystem scan, heartbeat data ni writes en idle. Solo hay I/O cuando se ejecuta la CLI o cuando `OPEN_APPLICATION` consulta explicitamente el catalogo.
