import React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { AppToastProvider } from "./app/AppToastProvider";
import { DeviceOperationProvider } from "./app/DeviceOperationState";
import "@fontsource/kodchasan/400.css";
import "@fontsource/kodchasan/700.css";
import "primeicons/primeicons.css";
import "primereact/resources/themes/lara-light-blue/theme.css";
import "primereact/resources/primereact.min.css";
import "./styles/theme.css";
import "./styles/global.css";

const rootElement = document.getElementById("root");

if (!rootElement) {
  throw new Error("Root element was not found.");
}

createRoot(rootElement).render(
  <React.StrictMode>
    <AppToastProvider>
      <DeviceOperationProvider>
        <App />
      </DeviceOperationProvider>
    </AppToastProvider>
  </React.StrictMode>
);
