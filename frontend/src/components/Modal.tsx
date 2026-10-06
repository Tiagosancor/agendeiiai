"use client";

import { useEffect, useRef, type ReactNode } from "react";

interface PropsModal {
  titulo: string;
  aberto: boolean;
  aoFechar: () => void;
  children: ReactNode;
}

export function Modal({ titulo, aberto, aoFechar, children }: PropsModal) {
  const dialogo = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    if (!aberto) return;
    const elemento = dialogo.current;
    const origem = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    elemento?.showModal();
    return () => {
      elemento?.close();
      // O nó do dialog é desmontado ao fechar; restaura explicitamente o foco.
      if (origem?.isConnected) origem.focus();
    };
  }, [aberto]);

  if (!aberto) return null;

  return (
      <dialog ref={dialogo} aria-modal="true" aria-label={titulo} onCancel={(evento) => { evento.preventDefault(); aoFechar(); }} className="painel-modal fixed inset-0 m-auto max-h-[90vh] w-[calc(100%_-_2rem)] max-w-md overflow-y-auto rounded-xl bg-white p-5 shadow-lg backdrop:bg-black/40 dark:bg-neutral-900">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-base font-semibold text-gray-900 dark:text-neutral-50">{titulo}</h2>
          <button
            type="button"
            onClick={aoFechar}
            aria-label="Fechar"
            className="text-gray-400 hover:text-gray-700 dark:hover:text-neutral-200"
          >
            ✕
          </button>
        </div>
        {children}
      </dialog>
  );
}
