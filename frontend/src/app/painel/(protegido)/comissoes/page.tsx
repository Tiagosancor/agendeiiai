"use client";

import { useCallback, useEffect, useState } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import type { ComissoesDoProfissional, ResumoComissaoProfissional, TotaisComissao } from "@/lib/tipos";
import { dataLocalIso, formatarReais } from "@/lib/formatacao";
import { MinhasQuinzenas, VisaoQuinzenas } from "@/components/painel/Quinzenas";

const TAMANHO_PAGINA = 20;

type Aba = "minhas" | "equipe" | "quinzenas";

const ROTULOS_ABA: Record<Aba, string> = { minhas: "Minhas comissões", equipe: "Equipe", quinzenas: "Quinzenas" };

type Periodo = { de: string; ate: string };

/** Atalhos da seção 7 ("Minhas comissões"): Hoje, Últimos 7 dias, Este mês, Mês anterior. */
const ATALHOS: { rotulo: string; periodo: () => Periodo }[] = [
  { rotulo: "Hoje", periodo: () => ({ de: dataLocalIso(), ate: dataLocalIso() }) },
  {
    rotulo: "Últimos 7 dias",
    periodo: () => {
      const hoje = new Date();
      return { de: dataLocalIso(new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate() - 6)), ate: dataLocalIso(hoje) };
    },
  },
  { rotulo: "Este mês", periodo: esteMes },
  {
    rotulo: "Mês anterior",
    periodo: () => {
      const hoje = new Date();
      return {
        de: dataLocalIso(new Date(hoje.getFullYear(), hoje.getMonth() - 1, 1)),
        ate: dataLocalIso(new Date(hoje.getFullYear(), hoje.getMonth(), 0)),
      };
    },
  },
];

function esteMes(): Periodo {
  const hoje = new Date();
  return {
    de: dataLocalIso(new Date(hoje.getFullYear(), hoje.getMonth(), 1)),
    ate: dataLocalIso(new Date(hoje.getFullYear(), hoje.getMonth() + 1, 0)),
  };
}

const formatarPercentual = (valor: number) => `${valor.toLocaleString("pt-BR", { maximumFractionDigits: 2 })}%`;

const formatarDataHora = (iso: string) =>
  new Date(iso).toLocaleString("pt-BR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });

export default function PaginaComissoes() {
  const { temPermissao } = useAutenticacao();
  const podeVerEquipe = temPermissao("VerComissoesDeTodos");
  const [aba, setAba] = useState<Aba>("minhas");
  const [periodo, setPeriodo] = useState<Periodo>(esteMes);

  return (
    <div className="space-y-6">
      <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Comissões</h1>

      {podeVerEquipe && (
        <div role="tablist" className="flex gap-2">
          {(["minhas", "equipe", "quinzenas"] as const).map((valor) => (
            <button
              key={valor}
              role="tab"
              aria-selected={aba === valor}
              onClick={() => setAba(valor)}
              className={`rounded-lg px-3 py-1.5 text-sm font-medium ${
                aba === valor
                  ? "bg-marca-primaria text-white"
                  : "text-gray-700 hover:bg-gray-100 dark:text-neutral-300 dark:hover:bg-neutral-800"
              }`}
            >
              {ROTULOS_ABA[valor]}
            </button>
          ))}
        </div>
      )}

      {aba === "quinzenas" && podeVerEquipe ? (
        <VisaoQuinzenas />
      ) : (
        <>
          <FiltroPeriodo periodo={periodo} aoMudar={setPeriodo} />
          {aba === "equipe" && podeVerEquipe ? <VisaoEquipe periodo={periodo} /> : <MinhasComissoes periodo={periodo} />}
        </>
      )}
    </div>
  );
}

function FiltroPeriodo({ periodo, aoMudar }: { periodo: Periodo; aoMudar: (p: Periodo) => void }) {
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2">
        {ATALHOS.map((atalho) => {
          const alvo = atalho.periodo();
          const ativo = alvo.de === periodo.de && alvo.ate === periodo.ate;
          return (
            <button
              key={atalho.rotulo}
              type="button"
              onClick={() => aoMudar(alvo)}
              aria-pressed={ativo}
              className={`rounded-full border px-3 py-1 text-xs font-medium ${
                ativo
                  ? "border-marca-primaria bg-marca-primaria/10 text-marca-primaria dark:border-marca-acento dark:text-marca-acento"
                  : "border-gray-300 text-gray-600 hover:bg-gray-50 dark:border-neutral-700 dark:text-neutral-300 dark:hover:bg-neutral-800"
              }`}
            >
              {atalho.rotulo}
            </button>
          );
        })}
      </div>
      <div className="grid grid-cols-2 gap-3 sm:flex">
        <label>
          <span className={classeLabel}>De</span>
          <input type="date" className={classeInput} value={periodo.de} max={periodo.ate} onChange={(e) => e.target.value && aoMudar({ ...periodo, de: e.target.value })} />
        </label>
        <label>
          <span className={classeLabel}>Até</span>
          <input type="date" className={classeInput} value={periodo.ate} min={periodo.de} onChange={(e) => e.target.value && aoMudar({ ...periodo, ate: e.target.value })} />
        </label>
      </div>
    </div>
  );
}

function MinhasComissoes({ periodo }: { periodo: Periodo }) {
  return (
    <div className="space-y-6">
      <MinhasQuinzenas />
      <ListaComissoes periodo={periodo} caminho="/painel/comissoes/minhas" />
    </div>
  );
}

/** Cartões de resumo + lista paginada — a mesma para "Minhas comissões" e para o detalhe de um profissional (visão do Administrador). */
function ListaComissoes({ periodo, caminho }: { periodo: Periodo; caminho: string }) {
  const { chamarApi } = useAutenticacao();
  const [pagina, setPagina] = useState(1);
  const [dados, setDados] = useState<ComissoesDoProfissional | null>(null);
  const [erro, setErro] = useState<string | null>(null);

  // Trocar o período volta para a primeira página.
  const [periodoAnterior, setPeriodoAnterior] = useState(periodo);
  if (periodoAnterior !== periodo) {
    setPeriodoAnterior(periodo);
    setPagina(1);
  }

  const carregar = useCallback(async () => {
    try {
      setErro(null);
      const parametros = new URLSearchParams({ de: periodo.de, ate: periodo.ate, pagina: String(pagina), tamanho: String(TAMANHO_PAGINA) });
      setDados(await chamarApi<ComissoesDoProfissional>(`${caminho}?${parametros.toString()}`));
    } catch {
      setErro("Não foi possível carregar as comissões.");
    }
  }, [chamarApi, caminho, periodo, pagina]);

  useEffect(() => {
    // Busca disparada pela troca de período/página.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  if (erro) return <p className="text-sm text-red-600 dark:text-red-400">{erro}</p>;
  if (!dados) return <p className="text-sm text-gray-500 dark:text-neutral-400">Carregando...</p>;

  if (!dados.profissionalId) {
    return (
      <p className="rounded-lg border border-gray-200 p-4 text-sm text-gray-600 dark:border-neutral-800 dark:text-neutral-300">
        Seu acesso não está ligado a um cadastro de profissional, então não há comissão de serviço para mostrar.
      </p>
    );
  }

  const totalPaginas = Math.max(1, Math.ceil(dados.totalItens / dados.tamanhoPagina));

  return (
    <div className="space-y-4">
      {dados.percentualAtual !== null && (
        <p className="text-sm text-gray-600 dark:text-neutral-300">
          Percentual atual: <strong>{formatarPercentual(dados.percentualAtual)}</strong>
          <span className="text-gray-500 dark:text-neutral-400"> — cada atendimento guarda o percentual do dia em que foi concluído.</span>
        </p>
      )}

      <CartoesTotais totais={dados.totais} />

      {dados.itens.length === 0 ? (
        <p className="text-sm text-gray-500 dark:text-neutral-400">Nenhum serviço concluído neste período.</p>
      ) : (
        <>
          {/* Celular: cartões empilhados. */}
          <ul className="space-y-2 sm:hidden">
            {dados.itens.map((item, i) => (
              <li key={`${item.agendamentoId}-${i}`} className={`${classeCartao} p-3`}>
                <div className="flex items-baseline justify-between gap-2">
                  <span className="font-medium text-gray-900 dark:text-neutral-50">{item.servico}</span>
                  <span className="font-semibold text-gray-900 dark:text-neutral-50">{formatarReais(item.comissao)}</span>
                </div>
                <div className="mt-1 text-xs text-gray-500 dark:text-neutral-400">
                  {formatarDataHora(item.inicio)} · {item.cliente}
                </div>
                <div className="mt-1 text-xs text-gray-600 dark:text-neutral-300">
                  {formatarReais(item.valorCobrado)} × {formatarPercentual(item.percentual)}
                </div>
              </li>
            ))}
          </ul>

          <div className={`${classeCartao} hidden sm:block`}>
            <table className="w-full">
              <thead className="bg-gray-50 dark:bg-neutral-800/50">
                <tr>
                  <th className={classeTh}>Data e hora</th>
                  <th className={classeTh}>Serviço</th>
                  <th className={classeTh}>Cliente</th>
                  <th className={`${classeTh} text-right`}>Valor cobrado</th>
                  <th className={`${classeTh} text-right`}>%</th>
                  <th className={`${classeTh} text-right`}>Comissão</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-neutral-800">
                {dados.itens.map((item, i) => (
                  <tr key={`${item.agendamentoId}-${i}`}>
                    <td className={classeTd}>{formatarDataHora(item.inicio)}</td>
                    <td className={classeTd}>{item.servico}</td>
                    <td className={classeTd}>{item.cliente}</td>
                    <td className={`${classeTd} text-right`}>{formatarReais(item.valorCobrado)}</td>
                    <td className={`${classeTd} text-right`}>{formatarPercentual(item.percentual)}</td>
                    <td className={`${classeTd} text-right font-medium`}>{formatarReais(item.comissao)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {totalPaginas > 1 && (
            <div className="flex items-center justify-between text-sm text-gray-600 dark:text-neutral-300">
              <button type="button" className={classeBotaoSecundario} disabled={pagina <= 1} onClick={() => setPagina((p) => p - 1)}>
                Anterior
              </button>
              <span>
                Página {pagina} de {totalPaginas}
              </span>
              <button type="button" className={classeBotaoSecundario} disabled={pagina >= totalPaginas} onClick={() => setPagina((p) => p + 1)}>
                Próxima
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function CartoesTotais({ totais }: { totais: TotaisComissao }) {
  const cartoes = [
    { rotulo: "Comissão no período", valor: formatarReais(totais.totalComissao), destaque: true },
    { rotulo: "Total atendido", valor: formatarReais(totais.totalAtendido), destaque: false },
    { rotulo: "Serviços", valor: String(totais.quantidadeServicos), destaque: false },
  ];

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
      {cartoes.map((cartao) => (
        <div key={cartao.rotulo} className={`${classeCartao} p-4`}>
          <div className="text-xs text-gray-500 dark:text-neutral-400">{cartao.rotulo}</div>
          <div
            className={`mt-1 font-semibold ${
              cartao.destaque ? "text-2xl text-marca-primaria dark:text-marca-acento" : "text-lg text-gray-900 dark:text-neutral-50"
            }`}
          >
            {cartao.valor}
          </div>
        </div>
      ))}
    </div>
  );
}

function VisaoEquipe({ periodo }: { periodo: Periodo }) {
  const { chamarApi } = useAutenticacao();
  const [resumo, setResumo] = useState<ResumoComissaoProfissional[] | null>(null);
  const [selecionado, setSelecionado] = useState<ResumoComissaoProfissional | null>(null);
  const [erro, setErro] = useState<string | null>(null);

  const carregar = useCallback(async () => {
    try {
      setErro(null);
      const parametros = new URLSearchParams({ de: periodo.de, ate: periodo.ate });
      setResumo(await chamarApi<ResumoComissaoProfissional[]>(`/painel/comissoes/resumo?${parametros.toString()}`));
    } catch {
      setErro("Não foi possível carregar o resumo da equipe.");
    }
  }, [chamarApi, periodo]);

  useEffect(() => {
    // Busca disparada pela troca de período.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  if (selecionado) {
    return (
      <div className="space-y-4">
        <button type="button" className={classeBotaoSecundario} onClick={() => setSelecionado(null)}>
          ← Voltar para a equipe
        </button>
        <h2 className="text-base font-semibold text-gray-900 dark:text-neutral-50">{selecionado.nome}</h2>
        <ListaComissoes periodo={periodo} caminho={`/painel/comissoes/profissionais/${selecionado.profissionalId}`} />
      </div>
    );
  }

  if (erro) return <p className="text-sm text-red-600 dark:text-red-400">{erro}</p>;
  if (!resumo) return <p className="text-sm text-gray-500 dark:text-neutral-400">Carregando...</p>;

  const totais = resumo.reduce<TotaisComissao>(
    (soma, r) => ({
      totalComissao: soma.totalComissao + r.totais.totalComissao,
      totalAtendido: soma.totalAtendido + r.totais.totalAtendido,
      quantidadeServicos: soma.quantidadeServicos + r.totais.quantidadeServicos,
    }),
    { totalComissao: 0, totalAtendido: 0, quantidadeServicos: 0 },
  );

  return (
    <div className="space-y-4">
      <CartoesTotais totais={totais} />

      <ul className="space-y-2">
        {resumo.map((r) => (
          <li key={r.profissionalId}>
            <button
              type="button"
              onClick={() => setSelecionado(r)}
              className={`${classeCartao} flex w-full items-center justify-between gap-3 p-3 text-left hover:bg-gray-50 dark:hover:bg-neutral-800/60`}
            >
              <span>
                <span className="block font-medium text-gray-900 dark:text-neutral-50">
                  {r.nome}
                  {!r.ativo && <span className="ml-2 text-xs font-normal text-gray-500 dark:text-neutral-400">(inativo)</span>}
                </span>
                <span className="block text-xs text-gray-500 dark:text-neutral-400">
                  {formatarPercentual(r.percentualAtual)} · {r.totais.quantidadeServicos} serviço(s) · {formatarReais(r.totais.totalAtendido)} atendido
                </span>
              </span>
              <span className="font-semibold text-gray-900 dark:text-neutral-50">{formatarReais(r.totais.totalComissao)}</span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
