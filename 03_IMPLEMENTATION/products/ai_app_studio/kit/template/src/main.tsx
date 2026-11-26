import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./ui/globals.css";
import "./app.css";
import { initTheme } from "./ui";

initTheme(); // preset + light/dark on <html>; keep it: the studio theme switcher relies on it
createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
