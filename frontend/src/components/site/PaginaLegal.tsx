import Link from "next/link";
import type { ReactNode } from "react";
import { BotaoTema } from "@/components/BotaoTema";

/**
 * Moldura das páginas legais do PRODUTO (Termos de Uso, Política de Privacidade — seção 6.4).
 * O texto é base, não revisado por advogado: a faixa "RASCUNHO" no topo é obrigatória até
 * a revisão, e nunca deve ser removida só para "ficar bonito".
 */
export function PaginaLegal({ titulo, nomeProduto, children }: { titulo: string; nomeProduto: string; children: ReactNode }) {
  return (
    <div className="min-h-screen">
      <header className="border-b border-gray-200 dark:border-neutral-800">
        <div className="mx-auto flex h-16 max-w-3xl items-center justify-between px-4">
          <Link href="/" className="flex items-center gap-2">
            <img src="/brand/agendeiiai-icone-reduzido.svg" alt="" width={28} height={28} className="rounded-lg" />
            <span className="font-display font-bold text-gray-900 dark:text-neutral-50">{nomeProduto}</span>
          </Link>
          <BotaoTema />
        </div>
      </header>
      <main className="mx-auto max-w-3xl px-4 py-10">
        <p
          role="note"
          data-testid="aviso-rascunho"
          className="rounded-lg border-2 border-dashed border-marca-madeira bg-amber-50 px-4 py-3 text-sm font-semibold text-amber-900 dark:border-marca-acento dark:bg-amber-950/40 dark:text-amber-100"
        >
          RASCUNHO — revisar antes de divulgar. Este texto é uma base e ainda não passou por revisão jurídica.
        </p>
        <h1 className="mt-8 font-display text-3xl font-bold text-gray-900 dark:text-neutral-50">{titulo}</h1>
        <div className="mt-6 space-y-4 text-sm leading-relaxed text-gray-700 dark:text-neutral-300 [&_h2]:pt-4 [&_h2]:text-base [&_h2]:font-semibold [&_h2]:text-gray-900 dark:[&_h2]:text-neutral-50 [&_ul]:list-disc [&_ul]:space-y-1 [&_ul]:pl-5">
          {children}
        </div>
      </main>
    </div>
  );
}
