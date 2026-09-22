"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

type Tema = "claro" | "escuro";

interface ContextoTemaValor {
  tema: Tema;
  alternarTema: () => void;
}

const ContextoTema = createContext<ContextoTemaValor | null>(null);

function aplicarClasse(tema: Tema) {
  document.documentElement.classList.toggle("dark", tema === "escuro");
  document.documentElement.setAttribute("data-tema-manual", "true");
}

/** Envolve a árvore inteira (`app/layout.tsx`) — o tema é global, painel e página pública compartilham o mesmo. */
export function ProvedorTema({ children }: { children: ReactNode }) {
  const [tema, setTema] = useState<Tema>("claro");

  useEffect(() => {
    // O script inline no <head> (evita FOUC — ver `scriptTemaInicial`) já aplicou a
    // classe certa antes da hidratação; aqui só sincroniza o estado do React com ela.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setTema(document.documentElement.classList.contains("dark") ? "escuro" : "claro");
  }, []);

  function alternarTema() {
    const proximo: Tema = tema === "claro" ? "escuro" : "claro";
    setTema(proximo);
    aplicarClasse(proximo);
    try {
      localStorage.setItem("tema", proximo);
    } catch {
      // Navegador privado/bloqueado — o tema só não persiste entre sessões, sem quebrar nada.
    }
  }

  return <ContextoTema.Provider value={{ tema, alternarTema }}>{children}</ContextoTema.Provider>;
}

export function useTema(): ContextoTemaValor {
  const contexto = useContext(ContextoTema);
  if (!contexto) throw new Error("useTema precisa estar dentro de <ProvedorTema>.");
  return contexto;
}

/**
 * Roda antes da hidratação (colado direto no `<head>`, seção "Text output"/FOUC) —
 * decide a classe `dark` a partir do que o usuário escolheu antes (localStorage) ou,
 * na primeira visita, da preferência do sistema.
 */
export const scriptTemaInicial = `(function () {
  try {
    var salvo = localStorage.getItem("tema");
    var prefereEscuro = window.matchMedia("(prefers-color-scheme: dark)").matches;
    var escuro = salvo ? salvo === "escuro" : prefereEscuro;
    if (escuro) document.documentElement.classList.add("dark");
    if (salvo) document.documentElement.setAttribute("data-tema-manual", "true");
  } catch (e) {}
})();`;
