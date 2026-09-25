"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { ErroApi, requisicaoApi } from "@/lib/api";
import type { EstadoAssinatura, Periodicidade, PlanoPublico } from "@/lib/tipos";
import { ROTULOS_ESTADO_ASSINATURA } from "@/lib/tipos";
import { dataLocalIso, formatarReais } from "@/lib/formatacao";
import { classeBotaoPerigo, classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";

interface NegocioNaPlataforma {
  id: string;
  nome: string;
  slug: string;
  tipo: string;
  plano: string | null;
  estado: EstadoAssinatura | null;
  fimTeste: string | null;
  proximoVencimento: string | null;
  cadastradoEm: string;
}

interface DetalheNegocio {
  negocio: NegocioNaPlataforma;
  planoId: string | null;
  periodicidade: Periodicidade | null;
  precoMensalTravado: number | null;
  valorDoPeriodo: number | null;
  carenciaAte: string | null;
  profissionaisAtivos: number;
  historico: { em: string; estadoAnterior: EstadoAssinatura | null; estadoNovo: EstadoAssinatura; autor: string; motivo: string }[];
  cobrancas: { pagoEm: string; valor: number; forma: string; periodoInicio: string; periodoFim: string; origem: string }[];
}

const FORMAS = ["Pix", "Boleto", "Cartao", "Transferencia", "Outro"];
const ESTADOS: EstadoAssinatura[] = ["EmTeste", "Ativa", "Atrasada", "Suspensa", "Cancelada"];

function data(iso: string | null): string {
  return iso ? new Date(iso).toLocaleDateString("pt-BR") : "—";
}

function hojeIso(): string {
  return dataLocalIso();
}

function somarMeses(diaIso: string, meses: number): string {
  const d = new Date(`${diaIso}T12:00:00`);
  d.setMonth(d.getMonth() + meses);
  return dataLocalIso(d);
}

/**
 * Administração da plataforma (seção 7) — só o dono do produto. Login próprio (token com
 * audiência própria, só em memória: recarregar a página pede login de novo). Mostra só
 * assinatura, nunca dados de clientes dos negócios.
 */
export function AdministracaoPlataforma() {
  const [token, setToken] = useState<string | null>(null);

  if (!token) return <LoginPlataforma aoEntrar={setToken} />;
  return <ListaNegocios token={token} aoSair={() => setToken(null)} />;
}

function LoginPlataforma({ aoEntrar }: { aoEntrar: (token: string) => void }) {
  const [email, setEmail] = useState("");
  const [senha, setSenha] = useState("");
  const [erro, setErro] = useState<string | null>(null);

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    try {
      const sessao = await requisicaoApi<{ accessToken: string }>("/plataforma/auth/login", { metodo: "POST", corpo: { email, senha } });
      aoEntrar(sessao.accessToken);
    } catch (excecao) {
      setErro(excecao instanceof ErroApi && excecao.status === 429 ? "Muitas tentativas. Aguarde um minuto." : "E-mail ou senha incorretos.");
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center px-4">
      <form onSubmit={enviar} className="w-full max-w-sm space-y-3 rounded-xl border border-gray-200 p-6 dark:border-neutral-800">
        <h1 className="font-display text-xl font-bold">Administração da plataforma</h1>
        <label className="block">
          <span className={classeLabel}>E-mail</span>
          <input type="email" required className={classeInput} value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="username" />
        </label>
        <label className="block">
          <span className={classeLabel}>Senha</span>
          <input type="password" required className={classeInput} value={senha} onChange={(e) => setSenha(e.target.value)} autoComplete="current-password" />
        </label>
        {erro && <p role="alert" className="text-sm text-red-700 dark:text-red-400">{erro}</p>}
        <button type="submit" className={`${classeBotaoPrimario} w-full`}>
          Entrar
        </button>
      </form>
    </main>
  );
}

function ListaNegocios({ token, aoSair }: { token: string; aoSair: () => void }) {
  const [filtro, setFiltro] = useState<EstadoAssinatura | "">("");
  const [negocios, setNegocios] = useState<NegocioNaPlataforma[] | null>(null);
  const [selecionado, setSelecionado] = useState<string | null>(null);

  const chamar = useCallback(
    async <T,>(caminho: string, metodo: "GET" | "POST" = "GET", corpo?: unknown): Promise<T> => {
      try {
        return await requisicaoApi<T>(caminho, { metodo, corpo, tokenAcesso: token });
      } catch (excecao) {
        if (excecao instanceof ErroApi && excecao.status === 401) aoSair();
        throw excecao;
      }
    },
    [token, aoSair],
  );

  const carregar = useCallback(async () => {
    setNegocios(await chamar<NegocioNaPlataforma[]>(`/plataforma/negocios${filtro ? `?estado=${filtro}` : ""}`));
  }, [chamar, filtro]);

  useEffect(() => {
    // Busca disparada pela montagem e pela troca do filtro.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  return (
    <main className="mx-auto max-w-6xl space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="font-display text-xl font-bold">Negócios</h1>
        <div className="flex items-center gap-2">
          <select aria-label="Filtrar por estado" className={classeInput} value={filtro} onChange={(e) => setFiltro(e.target.value as EstadoAssinatura | "")}>
            <option value="">Todos os estados</option>
            {ESTADOS.map((e) => (
              <option key={e} value={e}>
                {ROTULOS_ESTADO_ASSINATURA[e]}
              </option>
            ))}
          </select>
          <button onClick={aoSair} className={classeBotaoSecundario}>
            Sair
          </button>
        </div>
      </div>

      <div className={classeCartao}>
        <div className="overflow-x-auto">
          <table className="w-full min-w-200">
            <thead className="border-b border-gray-200 dark:border-neutral-800">
              <tr>
                <th className={classeTh}>Negócio</th>
                <th className={classeTh}>Plano</th>
                <th className={classeTh}>Estado</th>
                <th className={classeTh}>Fim do teste / vencimento</th>
                <th className={classeTh}>Cadastro</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 dark:divide-neutral-800">
              {negocios?.map((n) => (
                <tr key={n.id} className="cursor-pointer hover:bg-gray-50 dark:hover:bg-neutral-800" onClick={() => setSelecionado(n.id)}>
                  <td className={classeTd}>
                    <span className="font-medium">{n.nome}</span>
                    <span className="block text-xs text-gray-500">
                      {n.slug} · {n.tipo}
                    </span>
                  </td>
                  <td className={classeTd}>{n.plano ?? "—"}</td>
                  <td className={classeTd}>{n.estado ? ROTULOS_ESTADO_ASSINATURA[n.estado] : "Sem assinatura"}</td>
                  <td className={classeTd}>{n.estado === "EmTeste" ? data(n.fimTeste) : data(n.proximoVencimento)}</td>
                  <td className={classeTd}>{data(n.cadastradoEm)}</td>
                </tr>
              ))}
              {negocios?.length === 0 && (
                <tr>
                  <td className={classeTd} colSpan={5}>
                    Nenhum negócio neste filtro.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {selecionado && (
        <DetalheNegocioPlataforma
          negocioId={selecionado}
          chamar={chamar}
          aoFechar={() => setSelecionado(null)}
          aoAlterar={carregar}
        />
      )}
    </main>
  );
}

function DetalheNegocioPlataforma({
  negocioId,
  chamar,
  aoFechar,
  aoAlterar,
}: {
  negocioId: string;
  chamar: <T>(caminho: string, metodo?: "GET" | "POST", corpo?: unknown) => Promise<T>;
  aoFechar: () => void;
  aoAlterar: () => Promise<void>;
}) {
  const [detalhe, setDetalhe] = useState<DetalheNegocio | null>(null);
  const [planos, setPlanos] = useState<PlanoPublico[]>([]);
  const [mensagem, setMensagem] = useState<{ tipo: "ok" | "erro"; texto: string } | null>(null);

  const [valor, setValor] = useState("");
  const [forma, setForma] = useState("Pix");
  const [pagoEm, setPagoEm] = useState(hojeIso());
  const [inicio, setInicio] = useState(hojeIso());
  const [fim, setFim] = useState(somarMeses(hojeIso(), 1));
  const [dias, setDias] = useState("7");
  const [planoId, setPlanoId] = useState("");
  const [periodicidade, setPeriodicidade] = useState<Periodicidade>("Mensal");
  const [motivo, setMotivo] = useState("");

  const carregar = useCallback(async () => {
    const d = await chamar<DetalheNegocio>(`/plataforma/negocios/${negocioId}`);
    setDetalhe(d);
    setValor(d.valorDoPeriodo?.toFixed(2) ?? "");
    setPlanoId(d.planoId ?? "");
    setPeriodicidade(d.periodicidade ?? "Mensal");
    setFim(somarMeses(hojeIso(), d.periodicidade === "Anual" ? 12 : 1));
  }, [chamar, negocioId]);

  useEffect(() => {
    // Busca disparada pela montagem do detalhe.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
    requisicaoApi<PlanoPublico[]>("/cadastro/planos").then(setPlanos).catch(() => setPlanos([]));
  }, [carregar]);

  async function executar(acao: string, corpo: unknown, sucesso: string) {
    setMensagem(null);
    try {
      await chamar(`/plataforma/negocios/${negocioId}/${acao}`, "POST", corpo);
      setMensagem({ tipo: "ok", texto: sucesso });
      await Promise.all([carregar(), aoAlterar()]);
    } catch (excecao) {
      setMensagem({ tipo: "erro", texto: excecao instanceof ErroApi ? excecao.message : "Não foi possível concluir a ação." });
    }
  }

  if (!detalhe) return null;
  const n = detalhe.negocio;

  return (
    <section data-testid="detalhe-negocio-plataforma" className="space-y-4 rounded-xl border-2 border-marca-primaria/20 p-4 dark:border-marca-acento/30">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 className="font-display text-lg font-bold">{n.nome}</h2>
          <p className="text-sm text-gray-600 dark:text-neutral-400">
            {n.plano ?? "Sem plano"} · {detalhe.periodicidade?.toLowerCase() ?? "—"} ·{" "}
            {detalhe.precoMensalTravado !== null ? `${formatarReais(detalhe.precoMensalTravado)}/mês` : "—"} ·{" "}
            {n.estado ? ROTULOS_ESTADO_ASSINATURA[n.estado] : "Sem assinatura"} · {detalhe.profissionaisAtivos} profissionais ativos
          </p>
          <p className="text-sm text-gray-600 dark:text-neutral-400">
            Fim do teste: {data(n.fimTeste)} · Vencimento: {data(n.proximoVencimento)} · Carência até: {data(detalhe.carenciaAte)}
          </p>
        </div>
        <button onClick={aoFechar} aria-label="Fechar" className="text-gray-400 hover:text-gray-700">
          ✕
        </button>
      </div>

      {mensagem && (
        <p role="alert" className={mensagem.tipo === "erro" ? "text-sm text-red-700 dark:text-red-400" : "text-sm text-green-700 dark:text-green-400"}>
          {mensagem.texto}
        </p>
      )}

      <div className="grid gap-4 md:grid-cols-2">
        <form
          className="space-y-2 rounded-lg border border-gray-200 p-3 dark:border-neutral-800"
          onSubmit={(e) => {
            e.preventDefault();
            executar(
              "pagamentos",
              { valor: Number(valor), forma, pagoEm: `${pagoEm}T12:00:00Z`, periodoInicio: `${inicio}T00:00:00Z`, periodoFim: `${fim}T23:59:59Z` },
              "Pagamento registrado — assinatura ativa.",
            );
          }}
        >
          <h3 className="font-semibold">Registrar pagamento manual</h3>
          <div className="grid grid-cols-2 gap-2">
            <label>
              <span className={classeLabel}>Valor (R$)</span>
              <input className={classeInput} inputMode="decimal" value={valor} onChange={(e) => setValor(e.target.value.replace(",", "."))} required />
            </label>
            <label>
              <span className={classeLabel}>Forma</span>
              <select className={classeInput} value={forma} onChange={(e) => setForma(e.target.value)}>
                {FORMAS.map((f) => (
                  <option key={f}>{f}</option>
                ))}
              </select>
            </label>
            <label>
              <span className={classeLabel}>Pago em</span>
              <input type="date" className={classeInput} value={pagoEm} onChange={(e) => setPagoEm(e.target.value)} required />
            </label>
            <span />
            <label>
              <span className={classeLabel}>Período de</span>
              <input type="date" className={classeInput} value={inicio} onChange={(e) => setInicio(e.target.value)} required />
            </label>
            <label>
              <span className={classeLabel}>até</span>
              <input type="date" className={classeInput} value={fim} onChange={(e) => setFim(e.target.value)} required />
            </label>
          </div>
          <button type="submit" className={classeBotaoPrimario}>
            Registrar
          </button>
        </form>

        <div className="space-y-4">
          <form
            className="flex items-end gap-2 rounded-lg border border-gray-200 p-3 dark:border-neutral-800"
            onSubmit={(e) => {
              e.preventDefault();
              executar("estender-teste", { dias: Number(dias) }, "Teste estendido.");
            }}
          >
            <label className="flex-1">
              <span className={classeLabel}>Estender teste (dias)</span>
              <input type="number" min={1} max={90} className={classeInput} value={dias} onChange={(e) => setDias(e.target.value)} />
            </label>
            <button type="submit" className={classeBotaoSecundario}>
              Estender
            </button>
          </form>

          <form
            className="flex flex-wrap items-end gap-2 rounded-lg border border-gray-200 p-3 dark:border-neutral-800"
            onSubmit={(e) => {
              e.preventDefault();
              executar("plano", { planoId, periodicidade }, "Plano alterado.");
            }}
          >
            <label className="flex-1">
              <span className={classeLabel}>Trocar plano</span>
              <select className={classeInput} value={planoId} onChange={(e) => setPlanoId(e.target.value)}>
                {planos.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.nome}
                  </option>
                ))}
              </select>
            </label>
            <select aria-label="Periodicidade" className={`${classeInput} w-auto`} value={periodicidade} onChange={(e) => setPeriodicidade(e.target.value as Periodicidade)}>
              <option>Mensal</option>
              <option>Anual</option>
            </select>
            <button type="submit" className={classeBotaoSecundario}>
              Trocar
            </button>
          </form>

          <div className="flex flex-wrap items-end gap-2 rounded-lg border border-gray-200 p-3 dark:border-neutral-800">
            {n.estado === "Suspensa" ? (
              <button onClick={() => executar("reativar", undefined, "Reativada.")} className={classeBotaoSecundario}>
                Reativar
              </button>
            ) : (
              <>
                <label className="flex-1">
                  <span className={classeLabel}>Motivo da suspensão</span>
                  <input className={classeInput} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
                </label>
                <button
                  onClick={() => confirm(`Suspender ${n.nome}?`) && executar("suspender", { motivo }, "Suspensa.")}
                  className={classeBotaoPerigo}
                >
                  Suspender
                </button>
              </>
            )}
          </div>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <div>
          <h3 className="mb-1 font-semibold">Histórico</h3>
          <ul className="space-y-1 text-sm">
            {detalhe.historico.map((h) => (
              <li key={`${h.em}-${h.motivo}`}>
                <span className="text-gray-500">{new Date(h.em).toLocaleString("pt-BR")}</span> —{" "}
                {h.estadoAnterior && h.estadoAnterior !== h.estadoNovo ? `${ROTULOS_ESTADO_ASSINATURA[h.estadoAnterior]} → ` : ""}
                {ROTULOS_ESTADO_ASSINATURA[h.estadoNovo]}: {h.motivo} <span className="text-gray-500">({h.autor})</span>
              </li>
            ))}
          </ul>
        </div>
        <div>
          <h3 className="mb-1 font-semibold">Cobranças</h3>
          {detalhe.cobrancas.length === 0 ? (
            <p className="text-sm text-gray-500">Nenhuma.</p>
          ) : (
            <ul className="space-y-1 text-sm">
              {detalhe.cobrancas.map((c) => (
                <li key={`${c.pagoEm}-${c.valor}`}>
                  {data(c.pagoEm)} — {formatarReais(c.valor)} ({c.forma}, {c.origem}) · {data(c.periodoInicio)} a {data(c.periodoFim)}
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </section>
  );
}
