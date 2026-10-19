import Link from "next/link";
import type { ReactNode } from "react";
import { BotaoTema } from "@/components/BotaoTema";

/**
 * Moldura das páginas legais do PRODUTO (Termos de Uso, Política de Privacidade — seção 6.4).
 * O texto é base, não revisado por advogado: a faixa "RASCUNHO" no topo é obrigatória até
 * a revisão, e nunca deve ser removida só para "ficar bonito".
 */
export function PaginaLegal({
  titulo,
  nomeProduto,
  children,
  rascunho = true,
}: {
  titulo: string;
  nomeProduto: string;
  children: ReactNode;
  /** Só passe false com texto aprovado pelo dono: o padrão mantém a faixa de rascunho. */
  rascunho?: boolean;
}) {
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
        {rascunho && (
          <p
            role="note"
            data-testid="aviso-rascunho"
            className="rounded-lg border-2 border-dashed border-marca-madeira bg-amber-50 px-4 py-3 text-sm font-semibold text-amber-900 dark:border-marca-acento dark:bg-amber-950/40 dark:text-amber-100"
          >
            RASCUNHO — revisar antes de divulgar. Este texto é uma base e ainda não passou por revisão jurídica.
          </p>
        )}
        <h1 className={`${rascunho ? "mt-8" : "mt-0"} font-display text-3xl font-bold text-gray-900 dark:text-neutral-50`}>{titulo}</h1>
        <div className="mt-6 space-y-4 text-sm leading-relaxed text-gray-700 dark:text-neutral-300 [&_h2]:pt-4 [&_h2]:text-base [&_h2]:font-semibold [&_h2]:text-gray-900 dark:[&_h2]:text-neutral-50 [&_ul]:list-disc [&_ul]:space-y-1 [&_ul]:pl-5 [&_a]:underline [&_table]:w-full [&_table]:border-collapse [&_th]:border [&_th]:border-gray-300 [&_th]:px-3 [&_th]:py-2 [&_th]:text-left [&_th]:font-semibold [&_td]:border [&_td]:border-gray-300 [&_td]:px-3 [&_td]:py-2 [&_td]:align-top dark:[&_th]:border-neutral-700 dark:[&_td]:border-neutral-700">
          {children}
        </div>
      </main>
    </div>
  );
}
