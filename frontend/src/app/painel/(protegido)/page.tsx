"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import type { AlertasEstoque, PrimeirosPassos } from "@/lib/tipos";
import { CabecalhoCadastro } from "@/components/painel/CadastrosVisuais";
import "@/components/painel/painel-inicio.css";

export default function PaginaInicioPainel() {
  return (
    <div className="painel-cadastro painel-inicio">
      <CabecalhoCadastro titulo="Início" descricao="Bem-vindo. Use o menu para gerenciar a rotina do seu negócio." />
      <IndicadorEstoque />
      <div className="painel-inicio-grid">
        <ChecklistPrimeirosPassos />
        <section className="painel-inicio-agenda" aria-labelledby="inicio-agenda">
          <svg className="painel-inicio-icone" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M5 5h14v15H5zM8 2v6M16 2v6M5 10h14M8 14h3M8 17h6" /></svg>
          <h2 id="inicio-agenda">Agenda</h2>
          <p>Acompanhe seus horários e atendimentos.</p>
          <Link href="/painel/agenda" className="painel-inicio-ver-agenda">Ver agenda <span aria-hidden="true">↗</span></Link>
        </section>
      </div>
    </div>
  );
}

/**
 * Indicador de reposição (seção 7): quantos produtos estão esgotados e em estoque baixo, já na tela inicial.
 * Só para quem gerencia o estoque ou vende produtos, e só quando há algo a repor.
 */
function IndicadorEstoque() {
  const { chamarApi, temPermissao } = useAutenticacao();
  const podeVer = temPermissao("GerenciarEstoque") || temPermissao("VenderProdutos");
  const [alertas, setAlertas] = useState<AlertasEstoque | null>(null);

  useEffect(() => {
    if (!podeVer) return;
    chamarApi<AlertasEstoque>("/painel/estoque/alertas").then(setAlertas).catch(() => setAlertas(null));
  }, [chamarApi, podeVer]);

  if (!alertas || (alertas.esgotados.length === 0 && alertas.estoqueBaixo.length === 0)) return null;

  const conteudo = (
    <>
      <span className="font-semibold">Estoque:</span>{" "}
      {alertas.esgotados.length > 0 && (
        <span className="text-red-700 dark:text-red-400">
          {alertas.esgotados.length} {alertas.esgotados.length === 1 ? "produto esgotado" : "produtos esgotados"}
        </span>
      )}
      {alertas.esgotados.length > 0 && alertas.estoqueBaixo.length > 0 && " · "}
      {alertas.estoqueBaixo.length > 0 && (
        <span className="text-amber-700 dark:text-amber-400">
          {alertas.estoqueBaixo.length} com estoque baixo
        </span>
      )}
    </>
  );

  return (
    <div role="status" className="painel-inicio-alerta rounded-lg border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-gray-800 dark:border-amber-800 dark:bg-amber-950/40 dark:text-neutral-200">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M3 7l9-5 9 5v10l-9 5-9-5zM3 7l9 5 9-5M12 12v10" /></svg>
      <div>
      {temPermissao("GerenciarEstoque") ? (
        <Link href="/painel/estoque" className="hover:underline">
          {conteudo}
        </Link>
      ) : (
        conteudo
      )}
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
    <section data-testid="primeiros-passos" className="painel-inicio-passos" aria-labelledby="inicio-primeiros-passos">
      <div className="mb-3 flex items-start justify-between gap-3">
        <div>
          <h2 id="inicio-primeiros-passos" className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Primeiros passos</h2>
          <p className="text-sm text-gray-500 dark:text-neutral-400">{feitos} de 4 concluídos</p>
        </div>
        <button onClick={dispensar} className="text-sm text-gray-500 underline dark:text-neutral-400">
          Dispensar
        </button>
      </div>

      <div role="progressbar" aria-label="Primeiros passos concluídos" aria-valuemin={0} aria-valuemax={4} aria-valuenow={feitos} className="painel-inicio-progresso mb-3 h-1.5 rounded-full bg-gray-200 dark:bg-neutral-800">
        <div className="h-1.5 rounded-full bg-marca-acento transition-all" style={{ width: `${(feitos / 4) * 100}%` }} />
      </div>

      <ul className="painel-inicio-tarefas space-y-2 text-sm">
        {itens.map((item) => (
          <li key={item.texto} className="flex items-center gap-2">
            <Marcador feito={item.feito} />
            <span className="sr-only">{item.feito ? "Concluído: " : "Pendente: "}</span>
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
          <span className="sr-only">{passos.linkCopiado ? "Concluído: " : "Pendente: "}</span>
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
