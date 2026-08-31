# Validacion manual de performance

Esta guia cierra la optimizacion preventiva inicial de Prompt 14.5.

Principio vigente: no optimizar sin medir. Los numeros de memoria son budgets de ingenieria aproximados, no PASS/FAIL automatico ni unit tests.

## Herramientas locales

Windows:

- Task Manager: CPU, memoria, threads visibles por proceso en vista de detalles.
- Resource Monitor: actividad de disco y CPU por proceso.
- Performance Monitor / perfmon: `Process\Working Set`, `Process\Private Bytes`, `Process\% Processor Time`, `Process\Thread Count` e IO por proceso.
- PowerShell manual de diagnostico local:

```powershell
Get-Process -Name GaltekClassroom.Agent.Service,GaltekClassroom.Agent.Session |
  Select-Object Name,Id,WorkingSet64,PrivateMemorySize64,CPU,Threads,StartTime
```

Java / Master:

- `jcmd <PID> GC.heap_info`
- `jcmd <PID> VM.uptime`
- `jcmd <PID> Thread.print -l` solo durante diagnostico puntual.
- `jconsole`/JMX solo como herramienta manual si viene disponible con el JDK.

No integrar PowerShell, `jcmd`, JMX ni herramientas externas al producto.

## Diagnostico on-demand de Galtek

Agent Service en ejecucion, via IPC local read-only:

```powershell
.\GaltekClassroom.Agent.Session.exe --agent-runtime-diagnostics
```

Agent Service en modo consola/diagnostico del proceso actual:

```powershell
.\GaltekClassroom.Agent.Service.exe --runtime-diagnostics
```

Session Agent, diagnostico local del proceso que ejecuta el comando:

```powershell
.\GaltekClassroom.Agent.Session.exe --runtime-diagnostics
```

Master Java, snapshot local ligero por MXBeans sin levantar Spring:

```powershell
java -jar .\target\galtek-classroom-master-backend-0.1.0-SNAPSHOT.jar --runtime-diagnostics
```

Estos snapshots se calculan solo al pedirlos. No crean timer, scheduler, historico, SQLite, dashboard, Prometheus ni envio por heartbeat.

## A. Client LEGACY - idle

Preparacion:

- Esperar a que Windows estabilice despues del arranque.
- Confirmar Agent Service funcionando.
- Confirmar Session Agent activo.
- No ejecutar operaciones Galtek.

Medir:

- RAM del Agent Service.
- RAM del Session Agent.
- CPU aproximada por proceso.
- Threads.
- Actividad de disco observada.
- Wakeups evidentes si la herramienta disponible los muestra.

Objetivos de ingenieria existentes:

- Agent Service <= aprox. 60 MB idle.
- Session Agent <= aprox. 40 MB idle.
- Combinado <= aprox. 100 MB ideal.
- Mayor a aprox. 150 MB combinado requiere investigacion.

No convertir estos budgets en resultado automatico. Registrar hardware, version, tiempo desde boot y procesos Galtek medidos.

## B. Client LEGACY - Master offline

Comprobar:

- CPU idle cercana a 0.
- Reconnect con backoff.
- Sin log spam.
- Sin actividad continua de HDD atribuible a Galtek.

Observar durante varios minutos, incluyendo al menos dos ciclos de retry.

## C. Client LEGACY - Master online

Comprobar:

- Heartbeat cada 15 segundos.
- Sin writes periodicos persistentes por heartbeat.
- Consumo estable de memoria.
- CPU idle cercana a 0 fuera de los intercambios de heartbeat.

Usar Resource Monitor o perfmon para verificar que no aparezcan writes constantes en HDD.

## D. Master - 0 Clients

Comprobar:

- Master usable con 0 Clients.
- gRPC puede estar habilitado, pero no debe quedar scheduler de heartbeat activo sin conexiones.
- CPU practicamente idle.
- Heap estable.
- Sin crecimiento continuo de threads ni memoria.

Medir heap y uptime con `jcmd` o el snapshot local de MXBeans. No agregar flags JVM para cumplir este presupuesto sin evidencia real.

## E. Master - aprox. 26 Clients

Cuando exista entorno real:

- Conexiones ONLINE estables.
- Heap estable.
- Threads razonables.
- Heartbeat processing sin crecimiento continuo.
- SQLite sin writes por heartbeat.

Registrar duracion de la prueba, numero de Clients legacy/standard, estado de red LAN y cualquier operacion Galtek ejecutada.

## Startup

Medir manualmente:

- Tiempo hasta Agent Service `MINIMAL_READY`.
- Tiempo hasta Agent Service `SECURITY_READY`.
- Tiempo hasta Client `ONLINE`.
- Tiempo hasta Master control-plane ready.

Forma practica:

- Iniciar cronometro manual al arrancar proceso o servicio.
- Consultar `.\GaltekClassroom.Agent.Session.exe --ipc-status` hasta observar `startupPhase`.
- En Master, medir desde el inicio del proceso hasta que `GET /api/system/health` responda y SQLite este listo.

No agregar stopwatch persistente ni telemetria por este objetivo.

## CLASS_TIME_TO_READY

`CLASS_TIME_TO_READY` sigue siendo el KPI principal del producto.

Todavia no puede medirse completamente porque faltan workflows reales de:

- account preparation;
- workspace;
- browser;
- class context.

Se medira productivamente cuando esos workflows existan. No inventar benchmark ahora.

## Registro minimo

Para cada medicion guardar manualmente:

- Fecha y hora.
- Hardware medido.
- Version/commit de Galtek.
- Perfil esperado: `LEGACY`, `STANDARD` o `MASTER_BALANCED`.
- Escenario A-E.
- Valores observados.
- Hallazgos y si requieren investigacion.
