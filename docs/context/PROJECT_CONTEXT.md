# Contexto del proyecto

## Producto

Galtek Classroom es un software de administracion de aulas de computo y cibercafes orientado inicialmente a Windows. Una o varias computadoras Master administraran equipos Cliente dentro de una red local.

El producto queda orientado principalmente a maestras de kinder y primaria que administran alumnos pequenos. El flujo funcional debe minimizar acciones repetitivas computadora por computadora: la maestra ejecuta una accion sobre todos, un grupo, alumnos seleccionados o equipos seleccionados, y Galtek reporta exitos y excepciones por target.

## Hardware objetivo

El Master previsto de primaria es una PC de profesora con Intel Core i5 de 8a generacion aprox., 16 GB DDR4 y SSD de 256 GB. Debe absorber orquestacion, almacenamiento canonico de trabajos, metadata escolar, manifests/checksums futuros, distribucion de contenido, coordinacion batch, recuperacion, estado del aula y procesamiento administrativo razonable.

Los Clients previstos son mixtos: aprox. 16 equipos legacy con hardware heterogeneo muy limitado, principalmente 4 GB RAM + HDD y CPUs Core 2 Duo / Celeron / AMD antiguos, mas aprox. 10 equipos renovados con Core i5 6a generacion, 8 GB DDR4 y SSD de 256 GB. El diseno funcional debe operar sobre el peor Client sin hacer que los equipos rapidos esperen a los lentos.

Regla permanente: Galtek Classroom se disena primero para Clients de 4 GB RAM, HDD y CPU de gama baja. Cuando performance compite con una funcion secundaria, se degrada la funcion secundaria antes que afectar Windows, la aplicacion educativa del alumno o el control critico de la maestra.

Regla permanente desde Prompt 14.4: un corte de energia, reinicio abrupto o encendido masivo del aula es una condicion normal de operacion, no un caso raro. El sistema debe recuperar primero el plano de control local, reportar incertidumbre sin inventar exito, evitar trabajos visuales/pesados automaticos al boot y permitir operar los equipos disponibles sin esperar a todos los Clients.

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
- Perfiles operacionales de rendimiento `LEGACY` y `STANDARD` para Clients, con `LEGACY` como default conservador cuando el perfil es desconocido.
- Perfil operativo `MASTER_BALANCED` para mantener el Master pequeno, local y batch-friendly.
- Politicas futuras de load shedding con estado `DEGRADED` separado de `OFFLINE`.
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
- Comunicacion Master-Agent: gRPC y Protobuf v1 para conexion segura, identificacion, heartbeat, capabilities tipadas, framework de operaciones tipadas y status query read-only para reconciliar operaciones previas. `SHUTDOWN` y `RESTART` ya tienen ejecucion productiva en el Agent; las demas operaciones continuan pendientes.
- Seguridad de red: TLS/mTLS obligatorio con certificados ligados al trust de pairing por fingerprint de public key.
- Identidad criptografica local de Client: CNG/KSP de Windows a nivel maquina, con metadata publica separada.
- Identidad criptografica local de Master: metadata publica separada y private key cifrada fuera de SQLite/JSON plano.
- Descubrimiento futuro: mDNS/DNS-SD.
- IPC local Master Backend/Session Agent -> Agent Service read-only y canal local privilegiado Agent Service -> Session Agent: Windows Named Pipes.
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
- Existe transporte gRPC/mTLS para `ClientHello`, estado de conexion, heartbeat, capabilities tipadas, framework de operaciones y consulta read-only de resultado por `operationId`; `SHUTDOWN` y `RESTART` ya son operaciones productivas del Agent, el Master ya puede enviarlas por batch desde `POST /api/classrooms/{classroomId}/power-control` y reconciliar incertidumbre mediante `POST /api/operations/{operationId}/reconcile`. Todavia no existe mDNS real ni discovery real.
- Prompt 13 construyo transporte seguro usando el trust ya establecido; las fases siguientes no deben redisenar pairing.
- Prompt 14 construyo registro de Devices, capabilities y framework tipado de operaciones sobre este transporte, sin redisenar pairing/mTLS.
- Prompt 14.4 agrega resiliencia ante apagones, startup rapido, markers de ejecucion, escrituras atomicas/durables para archivos criticos y jitter de reconexion sin implementar comandos Windows reales.
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
- Ante perdida de energia, reinicio, desconexion o falta de ACK, no asumir `SUCCESS`; una operacion remota incierta requiere reconciliacion posterior.
- La reconciliacion de `SHUTDOWN`/`RESTART` inciertos pregunta por el resultado ORIGINAL usando el mismo `operationId`; nunca reenvia automaticamente una operacion destructiva para comprobar si funciono.
- `SHUTDOWN + OFFLINE` y `RESTART + reconnect` son evidencia operacional, pero no prueban `SUCCESS`; solo `OperationResult` o `OperationStatusReport KNOWN` pueden reconciliar exito.
- El plano de control tiene prioridad sobre el plano visual: Local IPC, identidad, trust, heartbeat/reconexion y acciones criticas futuras deben quedar disponibles antes que thumbnails, captura, proyeccion, inventario, transferencias grandes o sync pesado.
- Local IPC v1 permanece read-only. Las acciones interactivas futuras dentro de la sesion de usuario usan un canal separado Agent Service -> Session Agent, autenticado bilateralmente y con comandos tipados.
- El Master no espera a que todos los Clients arranquen para quedar operativo; se considera control-plane ready con proceso vivo y almacenamiento listo, aunque el conteo de Clients online sea cero.
- Boot, `ClientHello`, pairing, registration, reconnect y heartbeat no deben iniciar captura, proyeccion, thumbnails, filesystem sync ni inventario pesado automaticamente.
- Operaciones pesadas como distribucion, thumbnails o inventario nunca deben impedir operaciones `CRITICAL` como `UNLOCK_INPUT` o `STOP_PROJECTION`.
- Cuando Galtek no esta realizando trabajo solicitado, el Client debe quedar casi idle: CPU cercano a 0%, sin captura, sin scanning continuo, sin WMI periodico, sin writes periodicos y sin logs por heartbeat/PING sano.
- Los budgets de memoria son objetivos de ingenieria, no garantias contractuales: Agent Service preferiblemente <= 60 MB idle, Session Agent <= 40 MB idle, Client combinado <= 100 MB idle; superar aprox. 150 MB combinado en idle requiere justificacion y revision.
- Ningun perfil de hardware o performance concede autorizacion, trust, pairing ni permisos.
