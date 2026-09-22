"use client";

import type { ReactNode } from "react";

interface PropsModal {
  titulo: string;
  aberto: boolean;
  aoFechar: () => void;
  children: ReactNode;
}

export function Modal({ titulo, aberto, aoFechar, children }: PropsModal) {
  if (!aberto) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4">
      <div className="w-full max-w-md rounded-xl bg-white p-5 shadow-lg dark:bg-neutral-900">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-base font-semibold text-gray-900 dark:text-neutral-50">{titulo}</h2>
          <button
            onClick={aoFechar}
            aria-label="Fechar"
            className="text-gray-400 hover:text-gray-700 dark:hover:text-neutral-200"
          >
            ✕
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}
