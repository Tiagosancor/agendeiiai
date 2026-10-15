"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import { usePlataforma } from "@/components/plataforma/ContextoPlataforma";
import { ErroApi } from "@/lib/api";
import {
  classeStatus,
  formatarDataHora,
  formatarDuracao,
  mensagemErro,
  nivelResumo,
  rotuloStatus,
  rotuloTipo,
} from "@/lib/outbox-apresentacao";
import {
  CANAIS_OUTBOX,
  STATUS_OUTBOX,
  TIPOS_OUTBOX,
  criarOutboxPlataformaService,
  type FiltrosOutbox,
  type ItemOutbox,
  type PaginaOutbox,
  type ResumoOutbox,
} from "@/services/outbox-plataforma-service";

const FILTROS_INICIAIS: FiltrosOutbox = {
  status: "",
  tipo: "",
  canal: "",
  negocioId: "",
  de: "",
  ate: "",
  pagina: 1,
  tamanho: 50,
};

function mensagemFalha(erro: unknown): string {
  if (erro instanceof DOMException && erro.name === "AbortError") return "A consulta demorou demais ou foi substituída por uma mais recente.";
  if (erro instanceof ErroApi && erro.status === 403) return "Esta conta não possui acesso ao monitoramento de notificações.";
  if (erro instanceof ErroApi && erro.status >= 500) return "O serviço de monitoramento está indisponível no momento.";
  return "Não foi possível atualizar os dados. Verifique a conexão e tente novamente.";
}

function controladorComTimeout(milisegundos = 15_000) {
  const controlador = new AbortController();
  const temporizador = window.setTimeout(() => controlador.abort(), milisegundos);
  return { controlador, liberar: () => window.clearTimeout(temporizador) };
}

export function CartaoResumoOutbox() {
  const { chamar } = usePlataforma();
  const service = useMemo(() => criarOutboxPlataformaService(chamar), [chamar]);
  const [resumo, setResumo] = useState<ResumoOutbox | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [ultimaConsulta, setUltimaConsulta] = useState<Date | null>(null);
  const consultando = useRef(false);

  const consultar = useCallback(async () => {
    if (consultando.current) return;
    consultando.current = true;
    setCarregando(true);
    setErro(null);
    const { controlador, liberar } = controladorComTimeout();
    try {
      setResumo(await service.resumo(controlador.signal));
      setUltimaConsulta(new Date());
    } catch (excecao) {
      setErro(mensagemFalha(excecao));
    } finally {
      liberar();
      consultando.current = false;
      setCarregando(false);
    }
  }, [service]);

  useEffect(() => {
    // Consulta disparada pela montagem do cartão.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void consultar();
  }, [consultar]);

  useEffect(() => {
    const intervalo = window.setInterval(() => {
      if (document.visibilityState === "visible") void consultar();
    }, 60_000);
    return () => window.clearInterval(intervalo);
  }, [consultar]);

  const nivel = resumo ? nivelResumo(resumo) : "saudavel";
  const moldura = nivel === "critico"
    ? "border-red-300 dark:border-red-900"
    : nivel === "atencao"
      ? "border-amber-300 dark:border-amber-900"
      : "border-emerald-200 dark:border-emerald-900";

  return (
    <section data-testid="resumo-outbox" aria-labelledby="titulo-resumo-outbox" className={`${classeCartao} ${moldura} p-4`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h2 id="titulo-resumo-outbox" className="text-sm font-semibold">Saúde das notificações</h2>
            {resumo && (
              <span className={`rounded-full border px-2 py-0.5 text-xs font-medium ${nivel === "critico" ? classeStatus("Falhou") : nivel === "atencao" ? classeStatus("Pendente") : classeStatus("Enviada")}`}>
                {nivel === "critico" ? "Ação necessária" : nivel === "atencao" ? "Atenção" : "Saudável"}
              </span>
            )}
          </div>
          <p className="mt-1 text-xs text-gray-500 dark:text-neutral-400">
            {ultimaConsulta ? `Última consulta: ${formatarDataHora(ultimaConsulta.toISOString())}` : "Aguardando primeira consulta"}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button type="button" onClick={() => void consultar()} disabled={carregando} className={`${classeBotaoSecundario} min-h-11`}>
            {carregando ? "Atualizando..." : "Atualizar"}
          </button>
          <Link href="/plataforma/notificacoes" className={`${classeBotaoPrimario} inline-flex min-h-11 items-center`}>
            Ver detalhes
          </Link>
        </div>
      </div>

      {erro && <p role="alert" className="mt-3 text-sm text-red-700 dark:text-red-400">{erro} {resumo && "Os últimos dados válidos continuam exibidos."}</p>}
      {!resumo && carregando && <p role="status" className="mt-3 text-sm text-gray-600 dark:text-neutral-300">Consultando a outbox…</p>}
      {resumo && (
        <div className="mt-4 grid grid-cols-2 gap-3 lg:grid-cols-5">
          <Metrica rotulo="Pendentes" valor={resumo.pendentes} />
          <Metrica rotulo="Em processamento" valor={resumo.enviando} />
          <Metrica rotulo="Falhas" valor={resumo.falhas} destaque={resumo.falhas > 0} />
          <Metrica rotulo="Resultado desconhecido" valor={resumo.desconhecidas} destaque={resumo.desconhecidas > 0} />
          <Metrica rotulo="Pendência mais antiga" valor={formatarDuracao(resumo.idadeDaPendenciaMaisAntigaSegundos)} texto />
        </div>
      )}
    </section>
  );
}

function Metrica({ rotulo, valor, destaque = false, texto = false }: { rotulo: string; valor: number | string; destaque?: boolean; texto?: boolean }) {
  return (
    <div className={`rounded-lg border p-3 ${destaque ? "border-red-200 bg-red-50/70 dark:border-red-900 dark:bg-red-950/30" : "border-gray-200 bg-gray-50/60 dark:border-neutral-800 dark:bg-neutral-950/40"}`}>
      <p className="text-xs font-medium text-gray-600 dark:text-neutral-400">{rotulo}</p>
      <p className={`${texto ? "mt-1 text-sm" : "text-2xl"} font-bold text-gray-950 dark:text-white`}>{valor}</p>
    </div>
  );
}

export function MonitoramentoOutbox() {
  const { chamar, sair } = usePlataforma();
  const service = useMemo(() => criarOutboxPlataformaService(chamar), [chamar]);
  const [resumo, setResumo] = useState<ResumoOutbox | null>(null);
  const [pagina, setPagina] = useState<PaginaOutbox | null>(null);
  const [filtros, setFiltros] = useState(FILTROS_INICIAIS);
  const [rascunho, setRascunho] = useState(FILTROS_INICIAIS);
  const [carregandoResumo, setCarregandoResumo] = useState(true);
  const [carregandoLista, setCarregandoLista] = useState(true);
  const [erroResumo, setErroResumo] = useState<string | null>(null);
  const [erroLista, setErroLista] = useState<string | null>(null);
  const [ultimaConsulta, setUltimaConsulta] = useState<Date | null>(null);
  const resumoEmAndamento = useRef(false);
  const listaEmAndamento = useRef(false);
  const sequenciaLista = useRef(0);
  const abortarLista = useRef<AbortController | null>(null);

  const consultarResumo = useCallback(async () => {
    if (resumoEmAndamento.current) return;
    resumoEmAndamento.current = true;
    setCarregandoResumo(true);
    setErroResumo(null);
    const { controlador, liberar } = controladorComTimeout();
    try {
      setResumo(await service.resumo(controlador.signal));
      setUltimaConsulta(new Date());
    } catch (erro) {
      setErroResumo(mensagemFalha(erro));
    } finally {
      liberar();
      resumoEmAndamento.current = false;
      setCarregandoResumo(false);
    }
  }, [service]);

  const consultarLista = useCallback(async (consulta: FiltrosOutbox) => {
    abortarLista.current?.abort();
    const sequencia = ++sequenciaLista.current;
    const { controlador, liberar } = controladorComTimeout();
    abortarLista.current = controlador;
    listaEmAndamento.current = true;
    setCarregandoLista(true);
    setErroLista(null);
    try {
      const resposta = await service.listar(consulta, controlador.signal);
      if (sequencia === sequenciaLista.current && !controlador.signal.aborted) {
        setPagina(resposta);
        setUltimaConsulta(new Date());
      }
    } catch (erro) {
      if (sequencia === sequenciaLista.current) setErroLista(mensagemFalha(erro));
    } finally {
      liberar();
      if (sequencia === sequenciaLista.current) {
        listaEmAndamento.current = false;
        setCarregandoLista(false);
      }
    }
  }, [service]);

  useEffect(() => {
    // Consulta disparada pela montagem da página.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void consultarResumo();
  }, [consultarResumo]);

  useEffect(() => {
    // Consulta disparada pela montagem e pela aplicação de filtros/paginação.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void consultarLista(filtros);
    return () => abortarLista.current?.abort();
  }, [consultarLista, filtros]);

  useEffect(() => {
    const intervalo = window.setInterval(() => {
      if (document.visibilityState !== "visible") return;
      void consultarResumo();
      if (!listaEmAndamento.current) void consultarLista(filtros);
    }, 60_000);
    return () => window.clearInterval(intervalo);
  }, [consultarLista, consultarResumo, filtros]);

  function aplicarFiltros(evento: FormEvent) {
    evento.preventDefault();
    setFiltros({ ...rascunho, pagina: 1, tamanho: Math.min(200, Math.max(1, rascunho.tamanho)) });
  }

  function limparFiltros() {
    setRascunho(FILTROS_INICIAIS);
    setFiltros(FILTROS_INICIAIS);
  }

  function mudarPagina(novaPagina: number) {
    setFiltros((atuais) => ({ ...atuais, pagina: novaPagina }));
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  async function atualizarTudo() {
    await Promise.all([consultarResumo(), consultarLista(filtros)]);
  }

  const totalPaginas = pagina ? Math.max(1, Math.ceil(pagina.total / pagina.tamanho)) : 1;

  return (
    <main id="conteudo-plataforma" className="mx-auto max-w-7xl space-y-5 px-4 py-6">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <Link href="/plataforma" className="text-sm font-medium text-marca-primaria hover:underline dark:text-marca-acento">← Voltar aos negócios</Link>
          <h1 className="mt-2 font-display text-2xl font-bold">Notificações</h1>
          <p className="mt-1 max-w-3xl text-sm text-gray-600 dark:text-neutral-400">Acompanhamento somente leitura da entrega de e-mails e mensagens de WhatsApp. Nenhum dado pessoal é consultado nesta tela.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button type="button" onClick={() => void atualizarTudo()} disabled={carregandoResumo || carregandoLista} className={`${classeBotaoPrimario} min-h-11`}>
            {carregandoResumo || carregandoLista ? "Atualizando..." : "Atualizar"}
          </button>
          <button type="button" onClick={sair} className={`${classeBotaoSecundario} min-h-11`}>Sair</button>
        </div>
      </header>

      <p className="text-xs text-gray-500 dark:text-neutral-400">Última consulta: {ultimaConsulta ? formatarDataHora(ultimaConsulta.toISOString()) : "ainda não concluída"}. Atualização automática a cada 60 segundos enquanto esta aba estiver visível.</p>

      <ResumoDetalhado resumo={resumo} carregando={carregandoResumo} erro={erroResumo} aoTentar={consultarResumo} />

      <form onSubmit={aplicarFiltros} className={`${classeCartao} space-y-4 p-4`} aria-label="Filtros da outbox">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="font-display text-lg font-bold">Filtros</h2>
          <button type="button" onClick={limparFiltros} className="min-h-11 px-2 text-sm font-medium text-marca-primaria hover:underline dark:text-marca-acento">Limpar filtros</button>
        </div>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <CampoSelecao rotulo="Status" valor={rascunho.status} aoMudar={(status) => setRascunho((atual) => ({ ...atual, status }))} opcoes={STATUS_OUTBOX.map((valor) => ({ valor, texto: rotuloStatus(valor) }))} todos="Todos os status" />
          <CampoSelecao rotulo="Tipo" valor={rascunho.tipo} aoMudar={(tipo) => setRascunho((atual) => ({ ...atual, tipo }))} opcoes={TIPOS_OUTBOX.map((valor) => ({ valor, texto: rotuloTipo(valor) }))} todos="Todos os tipos" />
          <CampoSelecao rotulo="Canal" valor={rascunho.canal} aoMudar={(canal) => setRascunho((atual) => ({ ...atual, canal }))} opcoes={CANAIS_OUTBOX.map((valor) => ({ valor, texto: valor }))} todos="Todos os canais" />
          <label><span className={classeLabel}>ID do negócio</span><input className={classeInput} value={rascunho.negocioId} onChange={(evento) => setRascunho((atual) => ({ ...atual, negocioId: evento.target.value }))} placeholder="GUID do negócio" /></label>
          <label><span className={classeLabel}>Atualizada a partir de</span><input type="date" className={classeInput} value={rascunho.de} onChange={(evento) => setRascunho((atual) => ({ ...atual, de: evento.target.value }))} /></label>
          <label><span className={classeLabel}>Atualizada antes de</span><input type="date" className={classeInput} value={rascunho.ate} onChange={(evento) => setRascunho((atual) => ({ ...atual, ate: evento.target.value }))} /></label>
          <label><span className={classeLabel}>Itens por página</span><select className={classeInput} value={rascunho.tamanho} onChange={(evento) => setRascunho((atual) => ({ ...atual, tamanho: Number(evento.target.value) }))}><option value={25}>25</option><option value={50}>50</option><option value={100}>100</option><option value={200}>200</option></select></label>
        </div>
        <button type="submit" className={`${classeBotaoSecundario} min-h-11`}>Aplicar filtros</button>
      </form>

      <section aria-labelledby="titulo-listagem-outbox" className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 id="titulo-listagem-outbox" className="font-display text-lg font-bold">Mensagens da outbox</h2>
          {pagina && <p className="text-sm text-gray-600 dark:text-neutral-400">{pagina.total} {pagina.total === 1 ? "item" : "itens"}</p>}
        </div>
        {erroLista && <AvisoErro texto={erroLista} preservados={Boolean(pagina)} aoTentar={() => consultarLista(filtros)} />}
        {!pagina && carregandoLista && <div role="status" className={`${classeCartao} p-6 text-sm`}>Carregando mensagens…</div>}
        {pagina && pagina.itens.length === 0 && <div className={`${classeCartao} p-6 text-center`}><p className="font-medium">Nenhuma mensagem encontrada</p><p className="mt-1 text-sm text-gray-600 dark:text-neutral-400">A outbox está vazia para os filtros selecionados.</p></div>}
        {pagina && pagina.itens.length > 0 && <ListaOutbox itens={pagina.itens} />}
        {pagina && pagina.total > pagina.tamanho && (
          <nav aria-label="Paginação da outbox" className="flex items-center justify-center gap-3">
            <button type="button" className={`${classeBotaoSecundario} min-h-11`} disabled={pagina.pagina <= 1 || carregandoLista} onClick={() => mudarPagina(pagina.pagina - 1)}>Anterior</button>
            <span className="text-sm">Página {pagina.pagina} de {totalPaginas}</span>
            <button type="button" className={`${classeBotaoSecundario} min-h-11`} disabled={pagina.pagina >= totalPaginas || carregandoLista} onClick={() => mudarPagina(pagina.pagina + 1)}>Próxima</button>
          </nav>
        )}
      </section>
    </main>
  );
}

function CampoSelecao({ rotulo, valor, aoMudar, opcoes, todos }: { rotulo: string; valor: string; aoMudar: (valor: string) => void; opcoes: { valor: string; texto: string }[]; todos: string }) {
  return <label><span className={classeLabel}>{rotulo}</span><select className={classeInput} value={valor} onChange={(evento) => aoMudar(evento.target.value)}><option value="">{todos}</option>{opcoes.map((opcao) => <option key={opcao.valor} value={opcao.valor}>{opcao.texto}</option>)}</select></label>;
}

function ResumoDetalhado({ resumo, carregando, erro, aoTentar }: { resumo: ResumoOutbox | null; carregando: boolean; erro: string | null; aoTentar: () => Promise<void> }) {
  if (!resumo && carregando) return <div role="status" className={`${classeCartao} p-6 text-sm`}>Carregando resumo da outbox…</div>;
  if (!resumo && erro) return <AvisoErro texto={erro} preservados={false} aoTentar={aoTentar} />;
  if (!resumo) return <div className={`${classeCartao} p-6 text-sm`}>Nenhum dado de resumo disponível.</div>;
  return (
    <section aria-labelledby="titulo-metricas-outbox" className="space-y-3">
      <h2 id="titulo-metricas-outbox" className="sr-only">Resumo da saúde das notificações</h2>
      {erro && <AvisoErro texto={erro} preservados aoTentar={aoTentar} />}
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
        <Metrica rotulo="Pendentes" valor={resumo.pendentes} />
        <Metrica rotulo="Em processamento" valor={resumo.enviando} />
        <Metrica rotulo="Falhas" valor={resumo.falhas} destaque={resumo.falhas > 0} />
        <Metrica rotulo="Resultado desconhecido" valor={resumo.desconhecidas} destaque={resumo.desconhecidas > 0} />
        <Metrica rotulo="Pendência mais antiga" valor={formatarDuracao(resumo.idadeDaPendenciaMaisAntigaSegundos)} texto />
      </div>
      {resumo.desconhecidas > 0 && <p className="rounded-lg border border-fuchsia-200 bg-fuchsia-50 p-3 text-sm text-fuchsia-900 dark:border-fuchsia-900 dark:bg-fuchsia-950/30 dark:text-fuchsia-200">Resultado desconhecido significa que o provedor pode ter aceitado a mensagem, mas a confirmação se perdeu. A mensagem não é reenviada automaticamente.</p>}
    </section>
  );
}

function AvisoErro({ texto, preservados, aoTentar }: { texto: string; preservados: boolean; aoTentar: () => void | Promise<void> }) {
  return <div role="alert" className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950/30 dark:text-red-200"><span>{texto} {preservados && "Os últimos dados válidos continuam exibidos."}</span><button type="button" onClick={() => void aoTentar()} className="min-h-11 font-semibold underline">Tentar novamente</button></div>;
}

function ListaOutbox({ itens }: { itens: ItemOutbox[] }) {
  return (
    <>
      <div className={`${classeCartao} hidden lg:block`}>
        <div className="max-w-full overflow-x-auto">
        <table className="w-full min-w-240">
          <thead className="border-b border-gray-200 dark:border-neutral-800"><tr><th className={classeTh}>Status</th><th className={classeTh}>Tipo e canal</th><th className={classeTh}>Tentativas</th><th className={classeTh}>Atualização</th><th className={classeTh}>Próxima tentativa</th><th className={classeTh}>Motivo</th><th className={classeTh}>Identificadores</th></tr></thead>
          <tbody className="divide-y divide-gray-100 dark:divide-neutral-800">{itens.map((item) => <LinhaOutbox key={item.id} item={item} />)}</tbody>
        </table>
        </div>
      </div>
      <div className="grid gap-3 lg:hidden">{itens.map((item) => <CartaoItemOutbox key={item.id} item={item} />)}</div>
    </>
  );
}

function LinhaOutbox({ item }: { item: ItemOutbox }) {
  return <tr><td className={classeTd}><BadgeStatus status={item.status} /></td><td className={classeTd}><strong className="block">{rotuloTipo(item.tipo)}</strong><span className="text-xs text-gray-500 dark:text-neutral-400">{item.canal}</span></td><td className={classeTd}>{item.tentativas}</td><td className={classeTd}>{formatarDataHora(item.atualizadaEm)}</td><td className={classeTd}>{formatarDataHora(item.proximaTentativaEm)}</td><td className={`${classeTd} max-w-70`}><span title={item.ultimoErroCodigo ?? undefined}>{mensagemErro(item.ultimoErroCodigo)}</span></td><td className={classeTd}><Ids item={item} /></td></tr>;
}

function CartaoItemOutbox({ item }: { item: ItemOutbox }) {
  return <article className={`${classeCartao} space-y-3 p-4`}><div className="flex flex-wrap items-start justify-between gap-2"><div><h3 className="font-semibold">{rotuloTipo(item.tipo)}</h3><p className="text-sm text-gray-600 dark:text-neutral-400">{item.canal} · {item.tentativas} {item.tentativas === 1 ? "tentativa" : "tentativas"}</p></div><BadgeStatus status={item.status} /></div><dl className="grid grid-cols-2 gap-3 text-sm"><div><dt className="text-xs text-gray-500 dark:text-neutral-400">Atualização</dt><dd>{formatarDataHora(item.atualizadaEm)}</dd></div><div><dt className="text-xs text-gray-500 dark:text-neutral-400">Próxima tentativa</dt><dd>{formatarDataHora(item.proximaTentativaEm)}</dd></div></dl><p className="text-sm">{mensagemErro(item.ultimoErroCodigo)}</p><Ids item={item} /></article>;
}

function BadgeStatus({ status }: { status: string }) {
  return <span className={`inline-flex rounded-full border px-2 py-1 text-xs font-semibold ${classeStatus(status)}`}>{rotuloStatus(status)}</span>;
}

function abreviar(id: string) {
  return id.length > 12 ? `${id.slice(0, 8)}…${id.slice(-4)}` : id;
}

function Ids({ item }: { item: ItemOutbox }) {
  const ids = [{ rotulo: "Mensagem", valor: item.id }, { rotulo: "Negócio", valor: item.negocioId }, ...(item.agendamentoId ? [{ rotulo: "Agendamento", valor: item.agendamentoId }] : [])];
  return <div className="space-y-1">{ids.map(({ rotulo, valor }) => <div key={rotulo} className="flex items-center gap-2 text-xs"><span className="text-gray-500 dark:text-neutral-400">{rotulo}:</span><code title={valor}>{abreviar(valor)}</code><button type="button" onClick={() => void navigator.clipboard?.writeText(valor)} className="min-h-8 rounded px-1 font-medium text-marca-primaria hover:underline focus-visible:outline-2 dark:text-marca-acento" aria-label={`Copiar ID de ${rotulo.toLowerCase()}`}>Copiar</button></div>)}</div>;
}
