# Historial

## 2026-08-25 - Prompt 01

### Realizado

- Inicializada la estructura base del repositorio.
- Creado backend Master minimo con Spring Boot.
- Creada solucion .NET del Agent con Service, Session y Shared.
- Creado directorio `protocol/` con README de alcance.
- Creada memoria de agentes y README principal.

### Archivos principales modificados

- `README.md`
- `.gitignore`
- `master-backend/`
- `agent/`
- `protocol/README.md`
- `docs/context/`
- `docs/agent/`

### Decisiones tomadas

- Usar Spring Boot 3.5.16 para cumplir Spring Boot 3.x.
- Usar .NET 8 LTS para los proyectos del Agent.
- No agregar proyecto de tests .NET en Prompt 01 porque aun no hay comportamiento interno relevante fuera del arranque minimo.

### Cambios descartados

- No se implementaron licencias, gRPC, mTLS, mDNS, SQLite, React, Tauri ni funciones remotas.
- No se creo un `.proto` minimo para evitar fijar contratos prematuros.

### Pendiente

- Definir protocolo Protobuf inicial.
- Definir contratos IPC Service-Session.
- Agregar pruebas .NET cuando haya comportamiento validable.

### Validaciones

- `mvn clean verify` en `master-backend`: correcto, 1 prueba ejecutada.
- `mvn spring-boot:run` + `GET /api/system/health`: correcto.
- `dotnet build .\GaltekClassroom.Agent.sln`: correcto, 0 advertencias, 0 errores.
- `dotnet test .\GaltekClassroom.Agent.sln`: correcto; no habia proyectos de tests .NET.
- Agent Service ejecutado en modo consola y detenido limpiamente.
- Session Agent ejecutado y cerrado limpiamente.

### Commit sugerido

`chore: initialize Galtek Classroom architecture`
