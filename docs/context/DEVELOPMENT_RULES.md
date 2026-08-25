# Reglas de desarrollo

Estas reglas son obligatorias para todos los agentes futuros.

## Antes de modificar archivos

1. Leer primero:
   - `docs/context/PROJECT_CONTEXT.md`
   - `docs/context/ARCHITECTURE.md`
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
- `docs/agent/CURRENT_STATE.md` siempre.
- `docs/agent/DECISIONS.md` si hay decisiones vigentes nuevas.
- `docs/agent/HISTORY.md` siempre con una entrada compacta append-only.
