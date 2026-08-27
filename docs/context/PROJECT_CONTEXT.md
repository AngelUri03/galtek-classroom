# Contexto del proyecto

## Producto

Galtek Classroom es un software de administracion de aulas de computo y cibercafes orientado inicialmente a Windows. Una o varias computadoras Master administraran equipos Cliente dentro de una red local.

El producto queda orientado principalmente a maestras de kinder y primaria que administran alumnos pequenos. El flujo funcional debe minimizar acciones repetitivas computadora por computadora: la maestra ejecuta una accion sobre todos, un grupo, alumnos seleccionados o equipos seleccionados, y Galtek reporta exitos y excepciones por target.

## Problema que resuelve

Permite operar laboratorios con muchas computadoras desde una consola central, reduciendo pasos manuales para supervision, bloqueo, proyeccion, apertura controlada de aplicaciones, contenido escolar, asignacion de alumnos a equipos, recuperacion de trabajos y mantenimiento basico.

## Usuarios previstos

- Administradores de aulas de computo.
- Docentes o encargados de laboratorio.
- Maestras de kinder y primaria con alumnos pequenos.
- Operadores de cibercafe.
- Soporte tecnico local.

## Capacidades previstas

- Descubrimiento automatico en LAN.
- Multiples Masters.
- Master ligado a una cuenta Windows concreta mediante SID.
- Clientes administrados por Masters autorizados.
- Modelo independiente de Devices, Students y SchoolGroups.
- Student Workspaces pertenecientes al alumno.
- Browser profiles de alumno y Master sin almacenar contrasenas ni cookies.
- Miniaturas de pantallas de clientes.
- Vista en vivo de un cliente seleccionado.
- Proyeccion de pantalla del Master hacia clientes.
- Bloqueo y desbloqueo de teclado/mouse.
- Cuentas Windows administradas en Clients con slots logicos `PRIMARY` y `SECONDARY`.
- Cambio masivo futuro de sesion Windows administrada: consultar sesion, iniciar cuenta administrada, cerrar sesion y cambiar entre `PRIMARY`/`SECONDARY`.
- Inicio remoto de aplicaciones autorizadas.
- Apertura controlada de paginas web y YouTube mediante `OPEN_URL`.
- Distribucion de archivos a destinos logicos de workspace.
- Creacion masiva de carpetas de trabajo.
- Cambio y restauracion futura de wallpaper.
- Movimiento de alumno entre computadoras.
- Intercambio transaccional de alumnos entre computadoras.
- Recuperacion futura de trabajos de alumnos.
- Operaciones masivas con `PARTIAL_SUCCESS` y retry solo de fallidos.
- Apagado y reinicio remoto.
- Inicio automatico con Windows.
- Operacion local sin Internet.
- Licenciamiento mediante Galtek Hub.
- Seguridad criptografica entre equipos.
- Auditoria de operaciones administrativas.

## Tecnologias elegidas

- Master backend: Java 21, Spring Boot 3.x, Maven.
- Master UI futura: React + Tauri, sin Vite.
- Agent: C#/.NET en Windows.
- Comunicacion futura Master-Agent: gRPC y Protobuf.
- Seguridad futura de red: mTLS y certificados de dispositivo.
- Descubrimiento futuro: mDNS/DNS-SD.
- IPC local Service-Session Agent y Master Backend-Agent Service: Windows Named Pipes.
- Almacenamiento local Master: SQLite.

## Principios de producto

- Windows es la plataforma inicial.
- El sistema debe ser LAN/offline-first.
- Galtek Hub es el proveedor externo de licencias comerciales.
- Descubrimiento no implica confianza.
- La licencia comercial no reemplaza pairing, certificados ni autorizacion de red.
- El producto no debe convertirse en un canal de ejecucion remota arbitraria.
- `Device != Student`; los nombres visibles no son identidad.
- Los archivos de alumno pertenecen a `StudentWorkspace`, no a una PC especifica.
- El Master local se autoriza por licencia MASTER, Installation Identity y Windows SID ligado.
- Las operaciones futuras deben ser tipadas, batch-first, idempotentes cuando sea posible y con errores operacionales por target.
- Las operaciones futuras de cuentas Windows administradas enviaran solo `accountId` logico (`PRIMARY`/`SECONDARY`); el Master no almacenara ni enviara passwords.
- La credencial real futura de cuentas administradas pertenecera al Agent Service del Client y debera protegerse con mecanismos seguros de Windows.
- La persistencia local del dominio Master vive en SQLite y debe conservar historial e invariantes de assignments.
