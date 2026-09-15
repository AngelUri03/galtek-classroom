import React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import "@fontsource/kodchasan/400.css";
import "@fontsource/kodchasan/700.css";
import "./styles/theme.css";
import "./styles/global.css";

const rootElement = document.getElementById("root");

if (!rootElement) {
  throw new Error("Root element was not found.");
}

createRoot(rootElement).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
