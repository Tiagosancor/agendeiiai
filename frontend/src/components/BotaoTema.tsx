"use client";

import { useTema } from "@/lib/tema";

export function BotaoTema({ className = "" }: { className?: string }) {
  const { tema, alternarTema } = useTema();
  const escuro = tema === "dark";

  return (
    <button
      type="button"
      onClick={alternarTema}
      data-testid="botao-tema"
      aria-label={escuro ? "Ativar tema claro" : "Ativar tema escuro"}
      title={escuro ? "Tema claro" : "Tema escuro"}
      className={`rounded-lg p-2 text-gray-500 transition hover:bg-gray-100 dark:text-neutral-400 dark:hover:bg-neutral-800 ${className}`}
    >
      {/* Ícone escolhido pelo CSS (data-theme), não pelo estado React — assim fica certo já
          antes da hidratação. Lua no claro (vai pro escuro), sol no escuro. */}
      <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" className="dark:hidden">
        <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
      </svg>
      <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" className="hidden dark:block">
        <circle cx="12" cy="12" r="4" />
        <path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41" />
      </svg>
    </button>
  );
}
