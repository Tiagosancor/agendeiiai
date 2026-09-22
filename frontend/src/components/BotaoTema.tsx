"use client";

import { useTema } from "@/lib/tema";

export function BotaoTema({ className = "" }: { className?: string }) {
  const { tema, alternarTema } = useTema();

  return (
    <button
      type="button"
      onClick={alternarTema}
      aria-label={tema === "claro" ? "Ativar tema escuro" : "Ativar tema claro"}
      title={tema === "claro" ? "Tema escuro" : "Tema claro"}
      className={`rounded-lg p-2 text-sm text-gray-500 transition hover:bg-gray-100 dark:text-neutral-400 dark:hover:bg-neutral-800 ${className}`}
    >
      {tema === "claro" ? "🌙" : "☀️"}
    </button>
  );
}
