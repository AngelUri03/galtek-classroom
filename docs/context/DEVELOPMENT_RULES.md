# Reglas de desarrollo

Estas reglas son obligatorias para todos los agentes futuros.

## Antes de modificar archivos

1. Leer primero:
   - `docs/context/PROJECT_CONTEXT.md`
   - `docs/context/ARCHITECTURE.md`
   - `docs/context/FUNCTIONAL_MODEL.md`
   - `docs/context/DEVELOPMENT_RULES.md`
   - `docs/agent/CURRENT_STATE.md`
   - `docs/agent/DECISIONS.md`
2. Revisar despues:
   - `git status`
   - `git log --oneline -15`
3. Consultar `docs/agent/HISTORY.md` solo cuando se necesite contexto historico adicional.
4. Entender como se viene construyendo antes de modificar algo existente.
5. No rehacer arquitectura sin justificarlo.
6. No hacer commits automaticamente.

## Codigo

- Mantener cambios pequenos y coherentes con la estructura actual.
- No agregar dependencias innecesarias.
- Escribir codigo claro, simple y mantenible.
- Separar componentes y responsabilidades.
- En Java, mantener `controller -> service -> repository` cuando corresponda.
- No introducir repositorios, capas o abstracciones antes de que haya una necesidad real.
- Agregar pruebas proporcionales al riesgo del cambio.
- Mantener el proyecto compilando al terminar cada tarea.

## Seguridad

- Priorizar seguridad por defecto.
- No implementar ejecucion remota arbitraria.
- No aceptar comandos como `cmd.exe /c ...`, PowerShell arbitrario, shell remota ni rutas arbitrarias enviadas por un Master.
- Modelar futuros comandos como operaciones explicitas y estructuradas.
- No confiar en IP o MAC como identidad de autorizacion.
- No asumir que descubrimiento equivale a confianza.
- No asumir que una licencia MASTER autoriza control automatico sobre cualquier cliente.
- La autorizacion Master productiva proviene del Agent Service; el Master Backend solo consume estado derivado por IPC.
- Todo endpoint administrativo nuevo del Master Backend debe llamar a `MasterAccessGuard` antes de leer o escribir datos escolares.
- Solo quedan publicos sin `MasterAccessGuard` los endpoints de diagnostico `GET /api/system/health`, `GET /api/device/status`, `GET /api/device/machine-code` y `GET /api/master/authorization`.
- Nunca confiar en un SID declarado por JSON, UI, request HTTP o payload IPC.
- El SID local del caller debe derivarse del token real del cliente Named Pipe mediante APIs Windows soportadas.
- Cualquier error o duda en autorizacion Master debe fallar cerrado con `authorized=false`.
- Un administrador Windows distinto no hereda Master si su SID no esta ligado.
- `MasterWindowsBinding` nunca va en SQLite ni dentro de `installation.json` o `license.dat`.
- Rebinding de Master siempre debe ser explicito.

## Dominio funcional y UX masiva

- El usuario principal es una maestra que administra alumnos pequenos.
- Toda operacion repetitiva debe analizarse primero como operacion batch/grupo antes de disenar un flujo uno por uno.
- Nunca obligar a la maestra a repetir manualmente una operacion que ya tuvo exito en otros equipos.
- `PARTIAL_SUCCESS` debe manejarse explicitamente en operaciones masivas.
- Retry debe poder aplicarse unicamente a targets fallidos cuando el error sea recuperable.
- `Device` y `Student` son entidades independientes; no usar nombres de PC como identidad de alumno.
- Los archivos del alumno pertenecen a `StudentWorkspace`, no a `PC01`.
- Operaciones destructivas preservan el origen hasta verificar destino cuando haya transferencia de datos.
- Chrome passwords, cookies y cache no se copian directamente como estrategia de portabilidad.
- Un Master se autoriza por Windows SID ligado, no solo por username ni por pertenecer a Administrators.
- Errores tecnicos deben mapearse a errores operacionales antes de llegar a UI.
- Las futuras UI deben priorizar acciones masivas, recuperacion y minima intervencion manual.

## Persistencia Master SQLite

- Usar Spring JDBC y repositories explicitos; no introducir JPA/Hibernate mientras el diseno siga siendo SQLite local y explicito.
- Ejecutar migraciones con Flyway desde `db/migration/sqlite`.
- No crear ni modificar tablas fuera de migraciones versionadas.
- No usar `AUTOINCREMENT` para identidades del dominio; usar IDs `TEXT` generados por la aplicacion.
- Guardar timestamps en UTC como texto estable.
- Habilitar foreign keys por conexion.
- Mantener WAL, `busy_timeout` y pool pequeno para SQLite local.
- `DeviceAssignment` es fuente de verdad de asignaciones actuales e historicas.
- Mantener constraints de un assignment actual por alumno y un assignment actual por device.
- Archivar antes que borrar entidades escolares con historial; no hacer hard-delete de alumnos, devices, aulas o assignments salvo que una fase futura defina retencion/borrado seguro.
- No borrar, sobrescribir ni recrear automaticamente una base corrupta; reportar `MASTER_DATABASE_CORRUPT` y preservar el archivo para diagnostico/recuperacion.
- Mapear excepciones SQLite a `ErrorCode`; no propagar mensajes SQL tecnicos a UI.
- Minimizar PII: guardar solo datos necesarios para aula, alumno, workspace, perfiles y operaciones.
- No persistir passwords, cookies, tokens, cache protegido ni secretos de navegador.
- No persistir `MasterWindowsBinding` en `classroom.db`; la autoridad final del SID autorizado es el Agent Service.
- Consultas de listados deben ser batch-friendly; evitar N+1 para classroom, group, devices, assignments y targets batch.
- Bootstrap y snapshot deben devolver modelos de lectura agregados para la UI, no entidades de persistencia.
- Las escrituras multi-tabla deben ser transaccionales y tener pruebas de rollback cuando afecten invariantes.
- Usar version optimista en updates mutables y mapear conflictos a `CONCURRENT_MODIFICATION`.

## UI futura

- React.
- Tauri.
- Sin Vite.
- Diseno moderno orientado a escritorio.
- Adaptable a diferentes resoluciones de monitor.
- Navegacion completa mediante teclado.
- Foco visible.
- Enter solo cuando sea seguro.
- Escape para cerrar o cancelar.
- Orden de tabulacion logico.
- Restaurar foco tras cerrar modales.
- Evitar trampas de foco.
- Minimizar pasos operativos.

## Entorno y producto

- El entorno de desarrollo principal es Windows.
- Galtek Classroom debe ser LAN/offline-first.
- No introducir dependencias de AWS/cloud para el funcionamiento normal local.
- Galtek Hub es independiente y genera las licencias comerciales.
- La aplicacion local no genera licencias.

## Memoria de agentes

Antes de terminar cualquier tarea, actualizar los archivos de memoria que correspondan:

- `docs/context/ARCHITECTURE.md` si cambia arquitectura o estado implementado.
- `docs/context/FUNCTIONAL_MODEL.md` si cambia el dominio escolar, operaciones, errores o reglas batch.
- `docs/agent/CURRENT_STATE.md` siempre.
- `docs/agent/DECISIONS.md` si hay decisiones vigentes nuevas.
- `docs/agent/HISTORY.md` siempre con una entrada compacta append-only.
