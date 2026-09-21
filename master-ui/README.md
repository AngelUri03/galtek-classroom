# Galtek Classroom Master UI

Foundation del Master UI para escritorio.

## Stack

- React 18
- TypeScript
- Webpack 5
- Sin Vite
- Sin Tauri por ahora

## Comandos

```powershell
npm install
npm run typecheck
npm run build
npm start
```

El dashboard Aula consume datos reales del Master Backend mediante requests relativos a `/api`.
En desarrollo, Webpack dev server proxya `/api` a `http://127.0.0.1:8080`.
