"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import type { PrimeirosPassos } from "@/lib/tipos";

export default function PaginaInicioPainel() {
  return (
    <div className="space-y-6">
      <ChecklistPrimeirosPassos />
      <div>
        <h1 className="mb-2 text-lg font-semibold text-gray-900 dark:text-neutral-50">Bem-vindo</h1>
        <p className="text-sm text-gray-500 dark:text-neutral-400">
          Use o menu acima para gerenciar usuários, profissionais, serviços, clientes e o perfil do seu negócio.
        </p>
      </div>
    </div>
  );
}

/** Primeiro acesso (seção 6.5): some quando tudo foi feito ou quando o usuário dispensa. */
function ChecklistPrimeirosPassos() {
  const { chamarApi } = useAutenticacao();
  const [passos, setPassos] = useState<PrimeirosPassos | null>(null);
  const [copiado, setCopiado] = useState(false);

  useEffect(() => {
    chamarApi<PrimeirosPassos>("/painel/primeiros-passos").then(setPassos).catch(() => setPassos(null));
  }, [chamarApi]);

  if (!passos?.exibir) return null;

  async function copiarLink() {
    if (!passos) return;
    try {
      await navigator.clipboard.writeText(passos.linkAgendamento);
    } catch {
      // Sem permissão de área de transferência: o link continua visível para copiar à mão.
    }
    setCopiado(true);
    await chamarApi("/painel/primeiros-passos/link-copiado", { metodo: "POST" });
    setPassos({ ...passos, linkCopiado: true });
  }

  async function dispensar() {
    await chamarApi("/painel/primeiros-passos/dispensar", { metodo: "POST" });
    setPassos(null);
  }

  const itens = [
    { feito: passos.servicosCadastrados, texto: "Cadastre seus serviços", href: "/painel/servicos" },
    { feito: passos.profissionaisCadastrados, texto: "Cadastre a equipe", href: "/painel/profissionais" },
    { feito: passos.horariosConfigurados, texto: "Configure os horários de trabalho", href: "/painel/profissionais" },
  ];
  const feitos = itens.filter((i) => i.feito).length + (passos.linkCopiado ? 1 : 0);

  return (
    <section data-testid="primeiros-passos" className="rounded-xl border-2 border-marca-primaria/20 p-4 dark:border-marca-acento/30">
      <div className="mb-3 flex items-start justify-between gap-3">
        <div>
          <h2 className="font-display text-lg font-semibold text-gray-900 dark:text-neutral-50">Primeiros passos</h2>
          <p className="text-sm text-gray-500 dark:text-neutral-400">{feitos} de 4 concluídos</p>
        </div>
        <button onClick={dispensar} className="text-sm text-gray-500 underline dark:text-neutral-400">
          Dispensar
        </button>
      </div>

      <div className="mb-3 h-1.5 rounded-full bg-gray-200 dark:bg-neutral-800">
        <div className="h-1.5 rounded-full bg-marca-acento transition-all" style={{ width: `${(feitos / 4) * 100}%` }} />
      </div>

      <ul className="space-y-2 text-sm">
        {itens.map((item) => (
          <li key={item.texto} className="flex items-center gap-2">
            <Marcador feito={item.feito} />
            {item.feito ? (
              <span className="text-gray-500 line-through dark:text-neutral-500">{item.texto}</span>
            ) : (
              <Link href={item.href} className="font-medium text-marca-primaria underline dark:text-marca-acento">
                {item.texto}
              </Link>
            )}
          </li>
        ))}
        <li className="flex flex-wrap items-center gap-2">
          <Marcador feito={passos.linkCopiado} />
          <span className={passos.linkCopiado ? "text-gray-500 line-through dark:text-neutral-500" : "text-gray-800 dark:text-neutral-200"}>
            Compartilhe seu link de agendamento
          </span>
          <code className="rounded bg-gray-100 px-1.5 py-0.5 text-xs dark:bg-neutral-800">{passos.linkAgendamento}</code>
          <button onClick={copiarLink} className="text-sm font-medium text-marca-primaria underline dark:text-marca-acento">
            {copiado ? "Copiado" : "Copiar"}
          </button>
        </li>
      </ul>
    </section>
  );
}

function Marcador({ feito }: { feito: boolean }) {
  return (
    <span
      aria-hidden="true"
      className={`flex h-5 w-5 shrink-0 items-center justify-center rounded-full border text-xs ${
        feito ? "border-marca-acento bg-marca-acento text-white" : "border-gray-300 dark:border-neutral-600"
      }`}
    >
      {feito ? "✓" : ""}
    </span>
  );
}
