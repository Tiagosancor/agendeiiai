"use client";

import { use, useEffect, useRef, useState } from "react";
import { requisicaoApiPublica, ErroApi } from "@/lib/api";
import type { DetalhePublicoAgendamento, NegocioPublico } from "@/lib/tipos";
import { formatarReais } from "@/lib/formatacao";
import { formatarHorarioEstabelecimento, horarioEstabelecimentoParaIso } from "@/lib/horario-estabelecimento";

const ROTULO_STATUS: Record<string, string> = { EmAtendimento: "Em atendimento", Concluido: "Concluído" };
type Aviso = { tipo: "erro" | "sucesso" | "aviso"; texto: string };
type Dados = { detalhe: DetalhePublicoAgendamento; fuso: string };

export default function PaginaMeuAgendamento({ params }: { params: Promise<{ token: string }> }) {
  const { token } = use(params);
  // Cada token tem estado e locks próprios; desmontar invalida respostas antigas.
  return <GestaoAgendamento key={token} token={token} />;
}

function GestaoAgendamento({ token }: { token: string }) {
  const [dados, setDados] = useState<Dados | null>(null);
  const [erroCarregar, setErroCarregar] = useState<{ texto: string; temporario: boolean } | null>(null);
  const [tentativa, setTentativa] = useState(0);
  const [processando, setProcessando] = useState<"cancelar" | "remarcar" | "atualizar" | null>(null);
  const [mensagem, setMensagem] = useState<Aviso | null>(null);
  const [aguardandoAtualizacao, setAguardandoAtualizacao] = useState(false);
  const [novoInicio, setNovoInicio] = useState("");
  const trava = useRef(false);
  const montado = useRef(false);

  useEffect(() => {
    montado.current = true;
    return () => { montado.current = false; };
  }, []);

  useEffect(() => {
    let vigente = true;
    Promise.all([
      requisicaoApiPublica<DetalhePublicoAgendamento>(`/meus-agendamentos/${token}`),
      requisicaoApiPublica<NegocioPublico>("/negocio"),
    ]).then(([detalhe, negocio]) => {
      // Valida o fuso antes de habilitar ações, sem fallback para o aparelho.
      formatarHorarioEstabelecimento(detalhe.inicio, negocio.fuso);
      if (vigente) setDados({ detalhe, fuso: negocio.fuso });
    }).catch(excecao => {
      if (!vigente) return;
      const invalido = excecao instanceof ErroApi && (excecao.status === 401 || excecao.status === 404);
      setErroCarregar({
        texto: invalido ? "Link inválido ou expirado, ou agendamento não encontrado." : "Não foi possível carregar o agendamento. Tente novamente.",
        temporario: !invalido,
      });
    });
    return () => { vigente = false; };
  }, [token, tentativa]);

  async function atualizarDetalhes(): Promise<boolean> {
    try {
      const detalhe = await requisicaoApiPublica<DetalhePublicoAgendamento>(`/meus-agendamentos/${token}`);
      if (!montado.current) return false;
      setDados(atual => atual ? { ...atual, detalhe } : atual);
      setAguardandoAtualizacao(false);
      setMensagem({ tipo: "sucesso", texto: "Agendamento remarcado." });
      setNovoInicio("");
      return true;
    } catch {
      if (montado.current) setMensagem({ tipo: "aviso", texto: "A remarcação foi aceita, mas não foi possível atualizar os detalhes. Tente atualizar os detalhes novamente." });
      return false;
    }
  }

  async function repetirAtualizacao() {
    if (trava.current || !aguardandoAtualizacao) return;
    trava.current = true;
    setProcessando("atualizar");
    try { await atualizarDetalhes(); }
    finally { trava.current = false; if (montado.current) setProcessando(null); }
  }

  async function cancelar() {
    if (trava.current || aguardandoAtualizacao || dados?.detalhe.status !== "Agendado") return;
    trava.current = true;
    if (!confirm("Tem certeza que deseja cancelar este agendamento?")) { trava.current = false; return; }
    setProcessando("cancelar");
    setMensagem(null);
    try {
      await requisicaoApiPublica(`/meus-agendamentos/${token}/cancelar`, { metodo: "POST" });
      if (!montado.current) return;
      setDados(atual => atual ? { ...atual, detalhe: { ...atual.detalhe, status: "Cancelado" } } : atual);
      setMensagem({ tipo: "sucesso", texto: "Agendamento cancelado." });
    } catch (excecao) {
      if (montado.current) setMensagem({ tipo: "erro", texto: excecao instanceof ErroApi ? excecao.message : "Não foi possível cancelar. Tente novamente." });
    } finally {
      trava.current = false;
      if (montado.current) setProcessando(null);
    }
  }

  async function remarcar(evento: React.FormEvent) {
    evento.preventDefault();
    if (trava.current || aguardandoAtualizacao || !novoInicio || dados?.detalhe.status !== "Agendado") return;
    trava.current = true;
    setProcessando("remarcar");
    setMensagem(null);
    try {
      let iso: string;
      try { iso = horarioEstabelecimentoParaIso(novoInicio, dados.fuso); }
      catch (excecao) {
        setMensagem({ tipo: "erro", texto: excecao instanceof Error ? excecao.message : "Informe uma data e um horário válidos." });
        return;
      }
      await requisicaoApiPublica(`/meus-agendamentos/${token}/remarcar`, { metodo: "POST", corpo: { novoInicio: iso } });
      if (!montado.current) return;
      // POST aceito: apenas GET pode reconciliar o horário canônico.
      setAguardandoAtualizacao(true);
      setMensagem({ tipo: "aviso", texto: "Remarcação aceita. Atualizando os detalhes..." });
      await atualizarDetalhes();
    } catch (excecao) {
      if (montado.current) setMensagem({ tipo: "erro", texto: excecao instanceof ErroApi ? excecao.message : "Não foi possível remarcar. Tente novamente." });
    } finally {
      trava.current = false;
      if (montado.current) setProcessando(null);
    }
  }

  if (!dados) {
    return (
      <main className="flex min-h-screen flex-col items-center justify-center gap-4 px-6 text-center">
        <p role={erroCarregar ? "alert" : "status"} className="text-sm text-gray-500 dark:text-neutral-400">
          {erroCarregar?.texto ?? "Carregando..."}
        </p>
        {erroCarregar?.temporario && <button className="rounded-lg border border-gray-300 px-4 py-2 text-sm dark:border-neutral-700" onClick={() => {
          setErroCarregar(null);
          setTentativa(atual => atual + 1);
        }}>Tentar novamente</button>}
      </main>
    );
  }

  const { detalhe, fuso } = dados;
  const ativo = detalhe.status === "Agendado" && !aguardandoAtualizacao;
  return (
    <main className="mx-auto flex min-h-screen max-w-md flex-col justify-center gap-4 px-6 py-12">
      <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">{detalhe.nomeNegocio}</h1>
      <div className="rounded-xl border border-gray-200 p-4 dark:border-neutral-800">
        {aguardandoAtualizacao ? <p className="text-sm text-gray-600 dark:text-neutral-400">Detalhes aguardando atualização.</p> : <>
          <p className="text-sm text-gray-500 dark:text-neutral-400">Status: {ROTULO_STATUS[detalhe.status] ?? detalhe.status}</p>
          <p className="mt-1 text-sm text-gray-800 dark:text-neutral-200">{formatarHorarioEstabelecimento(detalhe.inicio, fuso)}</p>
          <p className="text-sm text-gray-600 dark:text-neutral-400">{detalhe.servicos.join(", ")}</p>
          <p className="text-sm text-gray-600 dark:text-neutral-400">{detalhe.local}</p>
          <p className="mt-2 text-sm font-semibold text-gray-900 dark:text-neutral-50">Total: {formatarReais(detalhe.total)}</p>
        </>}
      </div>
      <p id="fuso-estabelecimento" className="text-sm text-gray-600 dark:text-neutral-400">Horários do estabelecimento ({fuso}).</p>
      {mensagem && <p role={mensagem.tipo === "erro" ? "alert" : "status"} className="text-sm text-gray-700 dark:text-neutral-300">{mensagem.texto}</p>}
      {aguardandoAtualizacao && <button onClick={repetirAtualizacao} disabled={!!processando} className="rounded-lg border border-gray-300 px-4 py-2 text-sm disabled:opacity-60 dark:border-neutral-700">
        {processando ? "Atualizando..." : "Atualizar detalhes"}
      </button>}
      {ativo && <>
        <button onClick={cancelar} disabled={!!processando} className="rounded-lg border border-red-300 px-4 py-2 text-sm font-medium text-red-600 disabled:opacity-60 dark:border-red-900 dark:text-red-400">
          {processando === "cancelar" ? "Cancelando..." : "Cancelar agendamento"}
        </button>
        <form onSubmit={remarcar} className="space-y-2">
          <label htmlFor="novo-inicio" className="block text-sm text-gray-600 dark:text-neutral-400">Remarcar para</label>
          <input id="novo-inicio" type="datetime-local" required value={novoInicio} disabled={!!processando} aria-describedby="fuso-estabelecimento"
            onChange={e => setNovoInicio(e.target.value)} className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-neutral-700 dark:bg-neutral-900" />
          <button type="submit" disabled={!!processando} className="w-full rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-60">
            {processando === "remarcar" ? "Remarcando..." : "Remarcar"}
          </button>
        </form>
      </>}
    </main>
  );
}
