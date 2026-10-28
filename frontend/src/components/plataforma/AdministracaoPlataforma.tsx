"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { ErroApi, requisicaoApi } from "@/lib/api";
import type { DetalheNegocioNaPlataforma, NegocioNaPlataforma, EstadoAssinatura, EstadoConexaoWhatsApp, Periodicidade, PlanoPublico, SaudeWhatsApp, ServicoAgenteIaNegocio } from "@/lib/tipos";
import { ROTULOS_ESTADO_ASSINATURA } from "@/lib/tipos";
import { dataLocalIso, formatarReais } from "@/lib/formatacao";
import { classeBotaoPerigo, classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import { usePlataforma } from "@/components/plataforma/ContextoPlataforma";
import { ControleServicoAgenteIa, HistoricoServicoAgenteIa } from "@/components/plataforma/ServicoAgenteIa";
import { CartaoResumoOutbox } from "@/components/plataforma/MonitoramentoOutbox";

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
  const { token, sair } = usePlataforma();
  return <ListaNegocios token={token} aoSair={sair} />;
}

function ListaNegocios({ token, aoSair }: { token: string; aoSair: () => void }) {
  const versaoLista = useRef(0);
  const [filtroIa, setFiltroIa] = useState("");
  const [erroLista, setErroLista] = useState<string | null>(null);
  const [filtro, setFiltro] = useState<EstadoAssinatura | "">("");
  const [negocios, setNegocios] = useState<NegocioNaPlataforma[] | null>(null);
  const [selecionado, setSelecionado] = useState<string | null>(null);
  const [servicosAtualizados, setServicosAtualizados] = useState<Record<string, ServicoAgenteIaNegocio>>({});

  const chamar = useCallback(
    async <T,>(caminho: string, metodo: "GET" | "POST" | "PUT" = "GET", corpo?: unknown): Promise<T> => {
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
    const versao = ++versaoLista.current;
    const parametros = new URLSearchParams();
    if (filtro) parametros.set("estado", filtro);
    if (filtroIa) parametros.set("agenteIa", filtroIa);
    setErroLista(null);
    try {
      const dados = await chamar<NegocioNaPlataforma[]>(`/plataforma/negocios?${parametros}`);
      if (versao === versaoLista.current) setNegocios(dados);
    } catch {
      if (versao === versaoLista.current) setErroLista("Não foi possível carregar os negócios. Tente novamente.");
    }
  }, [chamar, filtro, filtroIa]);

  useEffect(() => {
    // Busca disparada pela montagem e pela troca do filtro.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void carregar();
  }, [carregar]);

  return (
    <main className="mx-auto max-w-6xl space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="font-display text-xl font-bold">Negócios</h1>
        <div className="flex flex-wrap items-center gap-2">
          <select aria-label="Filtrar por estado" className={classeInput} value={filtro} onChange={(e) => setFiltro(e.target.value as EstadoAssinatura | "")}>
            <option value="">Todos os estados</option>
            {ESTADOS.map((e) => (
              <option key={e} value={e}>
                {ROTULOS_ESTADO_ASSINATURA[e]}
              </option>
            ))}
          </select>
          <select aria-label="Filtrar por agente de IA" className={classeInput} value={filtroIa} onChange={e => setFiltroIa(e.target.value)}>
            <option value="">Todos os serviços de IA</option>
            <option value="true">Agente de IA ligado</option>
            <option value="false">Agente de IA desligado</option>
          </select>
          <button onClick={aoSair} className={classeBotaoSecundario}>
            Sair
          </button>
        </div>
      </div>

      {erroLista && <p role="alert">{erroLista} <button className={classeBotaoSecundario} onClick={() => void carregar()}>Tentar novamente</button></p>}
      <CartaoSaudeWhatsApp chamar={chamar} />
      <CartaoResumoOutbox />

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
                <th className={classeTh}>Agente de IA</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 dark:divide-neutral-800">
              {negocios?.map((n) => (
                <tr key={n.id} className="cursor-pointer hover:bg-gray-50 dark:hover:bg-neutral-800" onClick={() => setSelecionado(n.id)}>
                  <td className={classeTd}>
                    <button className="min-h-11 font-medium underline" onClick={e => { e.stopPropagation(); setSelecionado(n.id); }}>{n.nome}</button>
                    <span className="block text-xs text-gray-500">
                      {n.slug} · {n.tipo}
                    </span>
                  </td>
                  <td className={classeTd}>{n.plano ?? "—"}</td>
                  <td className={classeTd}>{n.estado ? ROTULOS_ESTADO_ASSINATURA[n.estado] : "Sem assinatura"}</td>
                  <td className={classeTd}>{n.estado === "EmTeste" ? data(n.fimTeste) : data(n.proximoVencimento)}</td>
                  <td className={classeTd}>{data(n.cadastradoEm)}</td>
                  <td className={classeTd} onClick={e => e.stopPropagation()}>
                    <ControleServicoAgenteIa negocio={n} aoAlterar={async servico => {
                      setServicosAtualizados(anteriores => ({ ...anteriores, [n.id]: servico }));
                      setNegocios(anteriores => anteriores?.map(item => item.id === n.id ? { ...item, agenteIaContratado: servico.contratado, agenteIaOrigem: servico.origem } : item) ?? null);
                      await carregar();
                    }} />
                  </td>
                </tr>
              ))}
              {negocios?.length === 0 && (
                <tr>
                  <td className={classeTd} colSpan={6}>
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
          key={selecionado}
          negocioId={selecionado}
          servicoAtualizado={servicosAtualizados[selecionado]}
          chamar={chamar}
          aoFechar={() => setSelecionado(null)}
          aoAlterar={carregar}
        />
      )}
    </main>
  );
}

const ROTULOS_CONEXAO: Record<EstadoConexaoWhatsApp, { texto: string; classe: string }> = {
  Conectada: { texto: "Conectada", classe: "bg-green-100 text-green-800 dark:bg-green-900/40 dark:text-green-300" },
  AguardandoQrCode: { texto: "Esperando novo QR code", classe: "bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-300" },
  Desconectada: { texto: "Desconectada", classe: "bg-red-100 text-red-800 dark:bg-red-900/40 dark:text-red-300" },
  Indisponivel: { texto: "Instância fora do ar", classe: "bg-red-100 text-red-800 dark:bg-red-900/40 dark:text-red-300" },
  NaoSeAplica: { texto: "Não se aplica", classe: "bg-gray-100 text-gray-700 dark:bg-neutral-800 dark:text-neutral-300" },
};

/**
 * Saúde da conexão do WhatsApp (seção 7): sem isso, a queda da sessão da instância só seria
 * percebida quando um cliente reclamasse de não ter recebido o código.
 */
function CartaoSaudeWhatsApp({ chamar }: { chamar: <T>(caminho: string) => Promise<T> }) {
  const [saude, setSaude] = useState<SaudeWhatsApp | null>(null);
  const [consultando, setConsultando] = useState(false);
  const [erro, setErro] = useState(false);

  const consultar = useCallback(async () => {
    setConsultando(true);
    setErro(false);
    try {
      setSaude(await chamar<SaudeWhatsApp>("/plataforma/whatsapp/saude"));
    } catch {
      setErro(true);
    } finally {
      setConsultando(false);
    }
  }, [chamar]);

  useEffect(() => {
    // Consulta disparada pela montagem do painel.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    consultar();
  }, [consultar]);

  const rotulo = saude ? ROTULOS_CONEXAO[saude.estado] : null;

  return (
    <section data-testid="saude-whatsapp" className={`${classeCartao} flex flex-wrap items-center justify-between gap-3 p-4`}>
      <div className="space-y-1">
        <h2 className="text-sm font-semibold">WhatsApp</h2>
        {erro && <p className="text-sm text-red-600">Não foi possível consultar a conexão.</p>}
        {saude && rotulo && (
          <>
            <p className="flex flex-wrap items-center gap-2 text-sm">
              <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${rotulo.classe}`}>{rotulo.texto}</span>
              <span className="text-gray-500 dark:text-neutral-400">Provedor: {saude.provedor}</span>
            </p>
            {saude.detalhe && <p className="text-xs text-gray-500 dark:text-neutral-400">{saude.detalhe}</p>}
          </>
        )}
      </div>
      <button onClick={consultar} disabled={consultando} className={classeBotaoSecundario}>
        {consultando ? "Consultando..." : "Consultar de novo"}
      </button>
    </section>
  );
}

function DetalheNegocioPlataforma({
  negocioId,
  servicoAtualizado,
  chamar,
  aoFechar,
  aoAlterar,
}: {
  negocioId: string;
  servicoAtualizado?: ServicoAgenteIaNegocio;
  chamar: <T>(caminho: string, metodo?: "GET" | "POST" | "PUT", corpo?: unknown) => Promise<T>;
  aoFechar: () => void;
  aoAlterar: () => Promise<void>;
}) {
  const [detalhe, setDetalhe] = useState<DetalheNegocioNaPlataforma | null>(null);
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
    const d = await chamar<DetalheNegocioNaPlataforma>(`/plataforma/negocios/${negocioId}`);
    setDetalhe(d);
    setValor(d.valorDoPeriodo?.toFixed(2) ?? "");
    setPlanoId(d.planoId ?? "");
    setPeriodicidade(d.periodicidade ?? "Mensal");
    setFim(somarMeses(hojeIso(), d.periodicidade === "Anual" ? 12 : 1));
  }, [chamar, negocioId]);

  useEffect(() => {
    // Busca disparada pela montagem do detalhe.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar().catch(() => setMensagem({ tipo: "erro", texto: "Não foi possível carregar o negócio." }));
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

  if (!detalhe) return mensagem ? <p role="alert">{mensagem.texto} <button className={classeBotaoSecundario} onClick={() => carregar().catch(() => setMensagem({ tipo: "erro", texto: "Não foi possível carregar o negócio." }))}>Tentar novamente</button></p> : <p>Carregando negócio…</p>;
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

      <HistoricoServicoAgenteIa negocioId={negocioId} servico={servicoAtualizado ?? detalhe.agenteIa} />

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
