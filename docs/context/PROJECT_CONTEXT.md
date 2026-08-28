# Contexto del proyecto

## Producto

Galtek Classroom es un software de administracion de aulas de computo y cibercafes orientado inicialmente a Windows. Una o varias computadoras Master administraran equipos Cliente dentro de una red local.

El producto queda orientado principalmente a maestras de kinder y primaria que administran alumnos pequenos. El flujo funcional debe minimizar acciones repetitivas computadora por computadora: la maestra ejecuta una accion sobre todos, un grupo, alumnos seleccionados o equipos seleccionados, y Galtek reporta exitos y excepciones por target.

## Hardware objetivo

El Master previsto de primaria es una PC de profesora con Intel Core i5 de 8a generacion aprox., 8 GB RAM y SSD de 500 GB. Debe absorber orquestacion, almacenamiento canonico de trabajos, metadata escolar, manifests/checksums futuros, distribucion de contenido, coordinacion batch, recuperacion, estado del aula y procesamiento administrativo razonable.

Los Clients previstos son mixtos: aprox. 16 equipos legacy con Celeron/Core Duo/Pentium o similar, 4 GB RAM y HDD de 256 GB, mas aprox. 10 equipos renovados con Core i5 6a generacion, 8 GB RAM y SSD de 256 GB. El diseno funcional debe operar sobre el peor Client sin hacer que los equipos rapidos esperen a los lentos.

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
- Pairing explicito Master-Client antes de permitir administracion remota.
- Modelo independiente de Devices, Students y SchoolGroups.
- Student Workspaces pertenecientes al alumno.
- Workspace canonico en Master con working copy local en Client mientras el alumno usa la PC.
- Browser profiles de alumno y Master sin almacenar contrasenas ni cookies.
- Miniaturas de pantallas de clientes.
- Vista en vivo de un cliente seleccionado.
- Proyeccion diferenciada por modo: screen share, whiteboard, pointer, media local y apertura local de contenido web.
- Bloqueo y desbloqueo de teclado/mouse.
- Cuentas Windows administradas en Clients con slots logicos `PRIMARY` y `SECONDARY`.
- `PRIMARY` y `SECONDARY` son Windows normal por default; no son kiosco ni implican bloqueo automatico.
- Cambio masivo futuro de sesion Windows administrada: consultar sesion, iniciar cuenta administrada, cerrar sesion y cambiar entre `PRIMARY`/`SECONDARY`.
- Inicio remoto de aplicaciones autorizadas.
- Apertura controlada de paginas web y YouTube mediante `OPEN_URL`.
- Distribucion batch de archivos a destinos logicos de workspace con apertura opcional posterior.
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
- Comunicacion Master-Agent: gRPC y Protobuf v1 para conexion segura, identificacion, heartbeat, capabilities tipadas y framework de operaciones sin ejecucion real todavia.
- Seguridad de red: TLS/mTLS obligatorio con certificados ligados al trust de pairing por fingerprint de public key.
- Identidad criptografica local de Client: CNG/KSP de Windows a nivel maquina, con metadata publica separada.
- Identidad criptografica local de Master: metadata publica separada y private key cifrada fuera de SQLite/JSON plano.
- Descubrimiento futuro: mDNS/DNS-SD.
- IPC local Service-Session Agent y Master Backend-Agent Service: Windows Named Pipes.
- Almacenamiento local Master: SQLite.

## Principios de producto

- Windows es la plataforma inicial.
- El sistema debe ser LAN/offline-first.
- Galtek Hub es el proveedor externo de licencias comerciales.
- Descubrimiento no implica confianza.
- Un Client descubierto no puede administrarse hasta completar pairing explicito.
- Discovery y pairing son fases distintas: discovery solo encuentra equipos; pairing establece confianza por intencion explicita.
- La licencia comercial no reemplaza pairing, certificados ni autorizacion de red.
- Una licencia MASTER valida no crea pairing con ningun Client.
- Las Network Identities criptograficas de Master y Client no generan confianza automatica entre equipos; prueban posesion de llaves durante pairing y sirven como base de los certificados mTLS ligados al trust.
- Network Identity no equivale a trust: el trust aparece solo despues de pairing y se persiste en ambos lados.
- El challenge/response de pairing demuestra posesion de las private keys del Master y del Client sin exponerlas.
- El pairing se revoca sin borrar Installation Identity ni Network Identity.
- Un pairing en estado `REVOKED` no puede administrar el Client.
- IP, MAC y hostname son datos informativos/de descubrimiento; no autorizan administracion.
- El Client inicia una conexion persistente saliente hacia el Master; el Master no depende de conexiones entrantes hacia cada PC Client.
- Existe transporte gRPC/mTLS minimo para `ClientHello`, estado de conexion, heartbeat, capabilities tipadas y framework de operaciones; todavia no existe mDNS real ni comandos remotos funcionales.
- Prompt 13 construyo transporte seguro usando el trust ya establecido; las fases siguientes no deben redisenar pairing.
- Prompt 14 construyo registro de Devices, capabilities y framework tipado de operaciones sobre este transporte, sin redisenar pairing/mTLS.
- El producto no debe convertirse en un canal de ejecucion remota arbitraria.
- `Network Identity != Pairing != Device != Student`.
- El Master genera y controla `deviceId`; nunca se confia en un `deviceId` declarado por el Client como identidad.
- `device_network_bindings` vincula Devices persistentes con Network Identities paired, pero el trust sigue viviendo en `paired-clients.json`.
- Un Client `PAIRED + ONLINE` sin Device se considera `AVAILABLE_FOR_REGISTRATION`; un Client `PAIRED + Device` queda `REGISTERED`; un trust `REVOKED` nunca es registrable ni administrable.
- Capabilities son informacion operativa, no autorizacion.
- `Device != Student`; los nombres visibles no son identidad.
- Los archivos de alumno pertenecen a `StudentWorkspace`, no a una PC especifica.
- El Master local se autoriza por licencia MASTER, Installation Identity y Windows SID ligado.
- Las operaciones futuras deben ser tipadas, batch-first, idempotentes cuando sea posible y con errores operacionales por target.
- Las operaciones futuras de cuentas Windows administradas enviaran solo `accountId` logico (`PRIMARY`/`SECONDARY`); el Master no almacenara ni enviara passwords.
- La credencial real futura de cuentas administradas pertenecera al Agent Service del Client y debera protegerse con mecanismos seguros de Windows.
- La persistencia local del dominio Master vive en SQLite y debe conservar historial e invariantes de assignments.
- El Master no debe convertirse en terminal server: Word, Chrome, Scratch, RoboMind, Office y aplicaciones interactivas corren localmente en cada Client.
- `PRIMARY` conserva escritorio Windows, mouse y teclado normales; Galtek agrega una capa de administracion de aula sobre Windows.
- `SECONDARY` conserva Windows normal; Galtek Service/Session Agent pueden permanecer en background sin restringir aplicaciones, archivos, sesion ni input salvo futura accion administrativa explicita.
- La asignacion Student -> Device sigue usando `DeviceAssignment` como fuente de verdad.
- La preparacion de equipos debe ser progresiva por Device: los primeros targets `READY` pueden empezar clase sin esperar a PCs lentas, offline o en recovery.
- El aula no tiene un unico boolean `READY`; debe representar conteos por target como `READY`, `PREPARING`, `OFFLINE`, `RECOVERY_REQUIRED` y `FAILED`.
- `CLASS_TIME_TO_READY` es KPI principal: minimizar el tiempo desde llegada/encendido hasta que los alumnos pueden iniciar actividad.
- El Master conserva el workspace canonico; el Client conserva una working copy local durante el uso y solo puede limpiarse despues de `SYNC -> VERIFY -> COMMIT CANONICAL -> CONFIRM`.
- Si falta confirmacion de sync, no asumir exito ni perdida: conservar working copy local y reportar `PENDING_SYNC` o `RECOVERY_REQUIRED`.
- Operaciones pesadas como distribucion, thumbnails o inventario nunca deben impedir operaciones `CRITICAL` como `UNLOCK_INPUT` o `STOP_PROJECTION`.
