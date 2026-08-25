# Contexto del proyecto

## Producto

Galtek Classroom es un software de administracion de aulas de computo y cibercafes orientado inicialmente a Windows. Una o varias computadoras Master administraran equipos Cliente dentro de una red local.

## Problema que resuelve

Permite operar laboratorios con muchas computadoras desde una consola central, reduciendo pasos manuales para supervision, bloqueo, proyeccion, apertura controlada de aplicaciones y mantenimiento basico.

## Usuarios previstos

- Administradores de aulas de computo.
- Docentes o encargados de laboratorio.
- Operadores de cibercafe.
- Soporte tecnico local.

## Capacidades previstas

- Descubrimiento automatico en LAN.
- Multiples Masters.
- Clientes administrados por Masters autorizados.
- Miniaturas de pantallas de clientes.
- Vista en vivo de un cliente seleccionado.
- Proyeccion de pantalla del Master hacia clientes.
- Bloqueo y desbloqueo de teclado/mouse.
- Inicio remoto de aplicaciones autorizadas.
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
- IPC local futuro Service-Session Agent: Named Pipes.
- Almacenamiento futuro Master: SQLite.

## Principios de producto

- Windows es la plataforma inicial.
- El sistema debe ser LAN/offline-first.
- Galtek Hub es el proveedor externo de licencias comerciales.
- Descubrimiento no implica confianza.
- La licencia comercial no reemplaza pairing, certificados ni autorizacion de red.
- El producto no debe convertirse en un canal de ejecucion remota arbitraria.
