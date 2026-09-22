20E.0B2-F2 - DIAGNOSTICO HISTORICO EXTERNO DE EARLY BOOT EN PC14

ESTADO: HISTORICO / NO REQUERIDO PARA EL FLUJO NORMAL

La politica productiva vigente es Automatic (Delayed Start). Este kit se
conserva solo como herramienta LAB opcional y no forma parte de instalacion,
upgrade, validacion o arranque normal. No agregar mas tracing como requisito
para cerrar SCM 7000/7009.

Codex preparo este kit en la laptop de desarrollo. Codex no tiene acceso a
PC14 y no ejecuto, desplego ni modifico nada en esa maquina. El usuario ejecuta
los pasos localmente en PC14 desde Windows PowerShell como administrador.

OBJETIVO Y LIMITES

F1 no distinguio entre dos fronteras: el apphost/CLR podria no alcanzar el
entrypoint administrado, o el entrypoint podria alcanzar el primer write
sincrono durable y bloquearse antes de persistir PROCESS_ENTER. F2 agrega
senales externas al proceso; no es un fix productivo ni un workaround.

Historicamente F2 no cambiaba Automatic, LocalSystem, ImagePath, dependencias, recovery,
ServicesPipeTimeout, DelayedAutoStart, Defender, Code Integrity, AppLocker,
trust, pairing, licencia, HTTP, Protobuf o Credential Provider. Tampoco inicia,
detiene, reinicia ni mata GaltekClassroomAgent.

ARCHIVOS F2

  06-arm-early-boot-trace.ps1
  07-collect-early-boot-trace.ps1
  08-restore-early-boot-trace.ps1
  20E.0B2-F2.wprp

Los scripts 01-05 y agent-service pertenecen al flujo F1 anterior. El artifact
F1 ya fue desplegado y validado antes de este retest; NO vuelva a ejecutar 02.

A. ARMAR F2

1. Copiar este directorio completo a la USB y abrir PowerShell elevado en PC14:

     Set-Location 'G:\GaltekClassroom\20E.0B2'

   Ajustar solo la letra de unidad si la USB usa otra. No editar rutas internas.

2. Mantener Ethernet desconectado. No ejecutar Start-Service. Ejecutar:

     powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\06-arm-early-boot-trace.ps1'

   Debe terminar con PASS para host tracing. La linea WPR dira
   WPR_BOOT_TRACE_ARMED o explicara que WPR no estuvo disponible/no pudo armarse.
   Si falla antes de PASS, conservar el error y no apagar a ciegas.

B. CAMBIOS TEMPORALES

06 crea, con acceso solo para SYSTEM y Administrators:

  C:\ProgramData\Galtek\Classroom\Diagnostics\20E.0B2-F2\

Agrega un valor Environment REG_MULTI_SZ solamente a la clave del servicio:

  HKLM\SYSTEM\CurrentControlSet\Services\GaltekClassroomAgent

Las unicas variables agregadas/reemplazadas son COREHOST_TRACE=1,
COREHOST_TRACEFILE apuntando a corehost-trace.log y
COREHOST_TRACE_VERBOSITY=4. El valor Environment previo se respalda dentro de
una subclave temporal ACL-protegida y nunca se imprime. No se crean variables
globales de maquina.

Por seguridad, si Environment ya contiene una entrada ajena a COREHOST, 06
falla antes de copiar o cambiar el valor: no intenta respaldar posibles secretos
de terceros. Entradas COREHOST anteriores si se respaldan y restauran exactamente.

Si wpr.exe existe y acepta el perfil, 06 registra un autologger para el siguiente
boot. El perfil captura process/thread, image load, disk I/O, file I/O y el
provider Microsoft-Windows-Services. Usa 20 MB de buffers definidos por el kit
(16 MB kernel + 4 MB eventos), file mode y temporales bajo el directorio F2.
No descarga ni instala WPR/ADK. Si WPR no esta disponible, host trace y logs de
Windows siguen siendo el fallback soportado por el kit.

C. COMPROBAR QUE QUEDO ARMADO

La salida de 06 debe contener:

  PASS: per-service .NET 8 host tracing armed for GaltekClassroomAgent.
  Service state was not changed: <estado previo>

Tambien debe mostrar el path exacto del trace y el estado WPR. No use reg.exe
para volcar Environment ni comparta su contenido. 06 verifica internamente las
tres entradas COREHOST sin imprimir otras variables.

D. APAGAR

Cerrar aplicaciones y apagar PC14 normalmente desde Windows. No usar restart,
hibernacion ni Fast Startup como sustituto de apagado completo. No ejecutar
Start-Service antes del apagado.

E. ETHERNET

Mantener el cable Ethernet fisicamente desconectado durante apagado, encendido,
inicio de sesion y collection. La falta de red no es el diagnostico bajo prueba;
solo conserva las condiciones del retest anterior.

F. COLD BOOT

Encender PC14 desde apagado completo y esperar a que Windows permita iniciar
sesion. No reconectar Ethernet.

G. NO START-SERVICE

No ejecutar Start-Service, Restart-Service, el script 02 ni ningun installer.
No intentar corregir el estado del servicio antes de recolectar. Eso destruiria
la separacion temporal del boot automatico.

H. RECOLECTAR

Abrir PowerShell elevado, volver al directorio del kit y ejecutar una sola vez:

  powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\07-collect-early-boot-trace.ps1'

07 no cambia el servicio. Guarda una carpeta collection-<timestamp> debajo de
20E.0B2-F2. Si WPR estaba armado, stopboot cierra y combina early-boot.etl.
Tambien conserva corehost-trace.log, su metadata, bootstrap del boot actual y
eventos desde LastBootUpTime: System/SCM, Application, Code Integrity,
AppLocker EXE and DLL, Defender y Security 4688 solo si ya existia por politica.
El script no habilita canales ni auditing. Canal deshabilitado o cero matches no
prueba que el componente no haya intervenido.

I. COPIAR Y CONSERVAR

Antes de restaurar o hacer Start-Service, copiar completa la carpeta
collection-<timestamp> a la USB. Mantener el ETL sin editar. El host trace puede
contener paths locales, argumentos, resolucion de runtime/assemblies y valores
de variables relacionadas con el host que este consulte. El kit no vuelca
manualmente el bloque Environment ni contenido de archivos Galtek. Aun asi,
tratar ETL y traces como material restringido y revisarlos antes de compartir;
no compartir si aparece una credencial, JWT, private key o dato inesperado.

J. RESTAURAR

Con la collection ya preservada, ejecutar:

  powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\08-restore-early-boot-trace.ps1'

08 restaura exactamente el Environment previo (o elimina el valor si antes no
existia), elimina la subclave temporal y ejecuta WPR cancelboot si 07 no retiro
ya el autologger mediante stopboot. Verifica la restauracion sin imprimir valores.
No elimina los artifacts recolectados. Debe terminar con PASS. Si falla, NO
adivinar ni editar Registry manualmente; conservar el error y pedir revision.
Si el servicio estuviera Running, su proceso conserva naturalmente el entorno
que heredo hasta terminar, pero ya no habra tracing en inicios futuros.

K. AISLAMIENTO MANUAL POSTERIOR

Solo despues del PASS de 08 queda permitido, en el mismo boot y aun offline:

  $timer = [Diagnostics.Stopwatch]::StartNew()
  Start-Service GaltekClassroomAgent
  $timer.Stop()
  "Start-Service elapsedMs=$($timer.ElapsedMilliseconds)"

Despues se pueden ejecutar 03 y 04. El script 04 filtra por LastBootUpTime de
forma predeterminada; para otro corte explicito use, por ejemplo:

  .\04-collect-network-state.ps1 -Since '2026-09-22T13:22:36.5-06:00'

No atribuir WER historicos ni MASTER_CONNECTION_* anteriores al boot actual.
No declarar REAL VALIDATED hasta analizar la evidencia externa del nuevo boot.
