import React from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./styles.css";

// Apply saved theme immediately to prevent flash
const savedTheme = localStorage.getItem("zta-theme") || "light";
document.documentElement.setAttribute("data-theme", savedTheme);

createRoot(document.getElementById("root")).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
