import { createRoot } from "react-dom/client";
import { App } from "./App";
import "./styles.css";

// No StrictMode: the mirror canvas is transferred to its worker exactly once.
createRoot(document.getElementById("root")!).render(<App />);
