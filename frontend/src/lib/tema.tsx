"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

/**
 * Fonte ÚNICA do tema: o atributo `data-theme` da <html>. Fundo (globals.css), variante
 * `dark:` do Tailwind (logo, textos, cartões) e o botão leem só ele — nunca uma media
 * query própria, senão fundo e logo podem divergir (bug real: texto escuro sobre fundo
 * escuro quando o sistema trocava de tema com a página aberta).
 */
export type Tema = "light" | "dark";

const CHAVE_ARMAZENAMENTO = "tema";
const CONSULTA_SISTEMA_ESCURO = "(prefers-color-scheme: dark)";

interface ContextoTemaValor {
  tema: Tema;
  alternarTema: () => void;
}

const ContextoTema = createContext<ContextoTemaValor | null>(null);

function lerTemaAplicado(): Tema {
  return document.documentElement.getAttribute("data-theme") === "dark" ? "dark" : "light";
}

function aplicarTema(tema: Tema) {
  document.documentElement.setAttribute("data-theme", tema);
}

function lerEscolhaSalva(): Tema | null {
  try {
    const salvo = localStorage.getItem(CHAVE_ARMAZENAMENTO);
    return salvo === "light" || salvo === "dark" ? salvo : null;
  } catch {
    return null;
  }
}

/** Envolve a árvore inteira (`app/layout.tsx`) — o tema é global, painel e página pública compartilham o mesmo. */
export function ProvedorTema({ children }: { children: ReactNode }) {
  const [tema, setTema] = useState<Tema>("light");

  useEffect(() => {
    // O script do <head> (`scriptTemaInicial`) já aplicou o data-theme antes da pintura;
    // aqui só sincroniza o estado do React com ele.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setTema(lerTemaAplicado());

    // Sem escolha manual, o sistema continua mandando — inclusive se ele trocar de tema
    // com a página aberta.
    const consulta = window.matchMedia(CONSULTA_SISTEMA_ESCURO);
    function aoMudarSistema(evento: MediaQueryListEvent) {
      if (lerEscolhaSalva()) return;
      const doSistema: Tema = evento.matches ? "dark" : "light";
      aplicarTema(doSistema);
      setTema(doSistema);
    }
    consulta.addEventListener("change", aoMudarSistema);
    return () => consulta.removeEventListener("change", aoMudarSistema);
  }, []);

  function alternarTema() {
    const proximo: Tema = lerTemaAplicado() === "dark" ? "light" : "dark";
    aplicarTema(proximo);
    setTema(proximo);
    try {
      localStorage.setItem(CHAVE_ARMAZENAMENTO, proximo);
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
 * Roda antes da pintura (colado direto no `<head>`, evita FOUC). Sempre grava um
 * data-theme: escolha salva, senão preferência do sistema, senão claro. Cada leitura tem
 * seu próprio try — um localStorage bloqueado não pode impedir de aplicar o do sistema.
 */
export const scriptTemaInicial = `(function () {
  var tema = null;
  try {
    var salvo = localStorage.getItem("${CHAVE_ARMAZENAMENTO}");
    if (salvo === "light" || salvo === "dark") tema = salvo;
  } catch (e) {}
  if (!tema) {
    try {
      tema = window.matchMedia("${CONSULTA_SISTEMA_ESCURO}").matches ? "dark" : "light";
    } catch (e) {
      tema = "light";
    }
  }
  document.documentElement.setAttribute("data-theme", tema);
})();`;
