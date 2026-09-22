"use client";

import { useEffect } from "react";

/** Registra o service worker (Sprint 5, PWA) — silencioso se o navegador não suportar. */
export function RegistroServiceWorker() {
  useEffect(() => {
    if ("serviceWorker" in navigator) {
      navigator.serviceWorker.register("/sw.js").catch(() => {
        // Falha de registro não deve quebrar a aplicação (ex.: navegador privado restrito).
      });
    }
  }, []);

  return null;
}
