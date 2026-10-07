"use client";

import { use, useEffect, useRef, useState } from "react";
import { requisicaoApiPublica, ErroApi } from "@/lib/api";
import type { DetalhePublicoAgendamento, NegocioPublico } from "@/lib/tipos";
import { formatarReais } from "@/lib/formatacao";
import { formatarHorarioEstabelecimento } from "@/lib/horario-estabelecimento";
import { controleGestao, RemarcacaoAgendamento } from "@/components/publico/RemarcacaoAgendamento";

const ROTULO_STATUS: Record<string, string> = { EmAtendimento: "Em atendimento", Concluido: "Concluído" };
type Aviso = { tipo: "erro" | "sucesso" | "aviso"; texto: string };
type Dados = { detalhe: DetalhePublicoAgendamento; fuso: string };

export default function PaginaMeuAgendamento({ params }: { params: Promise<{ token: string }> }) {
  const { token } = use(params);
  return <GestaoAgendamento key={token} token={token} />;
}

function GestaoAgendamento({ token }: { token: string }) {
  const [dados, setDados] = useState<Dados | null>(null);
  const [erroCarregar, setErroCarregar] = useState<{ texto: string; temporario: boolean } | null>(null);
  const [tentativa, setTentativa] = useState(0);
  const [processando, setProcessando] = useState<"cancelar" | "remarcar" | "atualizar" | null>(null);
  const [mensagem, setMensagem] = useState<Aviso | null>(null);
  const [aguardandoAtualizacao, setAguardandoAtualizacao] = useState(false);
  const [aberto, setAberto] = useState(false);
  const trava = useRef(false);
  const montado = useRef(false);
  const acaoAceita = useRef<"cancelar" | "remarcar">("remarcar");
  const titulo = useRef<HTMLHeadingElement>(null);
  const carga = useRef<HTMLDivElement>(null);
  const abrir = useRef<HTMLButtonElement>(null);

  useEffect(() => { montado.current = true; return () => { montado.current = false; }; }, []);
  useEffect(() => {
    if (mensagem?.tipo === "erro" && document.getElementById("titulo-remarcacao")) document.getElementById("titulo-remarcacao")?.focus();
    else if (dados || erroCarregar) (titulo.current ?? carga.current)?.focus();
  }, [dados, erroCarregar, mensagem]);
  useEffect(() => {
    let vigente = true;
    (async () => {
      const detalhe = await requisicaoApiPublica<DetalhePublicoAgendamento>(`/meus-agendamentos/${token}`);
      const fuso = detalhe.fuso ?? (await requisicaoApiPublica<NegocioPublico>("/negocio")).fuso;
      formatarHorarioEstabelecimento(detalhe.inicio, fuso);
      if (vigente) setDados({ detalhe, fuso });
    })().catch(excecao => {
      if (!vigente) return;
      const invalido = excecao instanceof ErroApi && (excecao.status === 401 || excecao.status === 404);
      setErroCarregar({ texto: invalido ? "Link inválido ou expirado, ou agendamento não encontrado." : "Não foi possível carregar o agendamento. Tente novamente.", temporario: !invalido });
    });
    return () => { vigente = false; };
  }, [token, tentativa]);

  function aplicar(detalhe: DetalhePublicoAgendamento) {
    const fuso = detalhe.fuso ?? dados!.fuso;
    formatarHorarioEstabelecimento(detalhe.inicio, fuso);
    setDados({ detalhe, fuso }); setAguardandoAtualizacao(false); setAberto(false);
    setMensagem({ tipo: "sucesso", texto: acaoAceita.current === "cancelar" ? "Agendamento cancelado." : "Agendamento remarcado." });
  }
  async function atualizarDetalhes() {
    try {
      const detalhe = await requisicaoApiPublica<DetalhePublicoAgendamento>(`/meus-agendamentos/${token}`);
      if (montado.current) aplicar(detalhe);
    } catch {
      if (montado.current) setMensagem({ tipo: "aviso", texto: `A ${acaoAceita.current === "cancelar" ? "solicitação de cancelamento" : "remarcação"} foi aceita, mas não foi possível atualizar os detalhes. Tente atualizar os detalhes novamente.` });
    }
  }
  async function repetirAtualizacao() {
    if (trava.current || !aguardandoAtualizacao) return;
    trava.current = true; setProcessando("atualizar"); titulo.current?.focus();
    try { await atualizarDetalhes(); } finally { trava.current = false; if (montado.current) setProcessando(null); }
  }
  async function agir(acao: "cancelar" | "remarcar", slot?: string): Promise<boolean> {
    if (trava.current || aguardandoAtualizacao || dados?.detalhe.acoes?.[acao]?.permitido !== true) return false;
    trava.current = true;
    if (acao === "cancelar" && !confirm("Tem certeza que deseja cancelar este agendamento?")) { trava.current = false; return false; }
    setProcessando(acao); setMensagem(null);
    try {
      const detalhe = await requisicaoApiPublica<DetalhePublicoAgendamento | undefined>(`/meus-agendamentos/${token}/${acao}`, {
        metodo: "POST", ...(slot ? { corpo: { novoInicio: slot } } : {}), cabecalhos: { Prefer: "return=representation" },
      });
      if (!montado.current) return true;
      acaoAceita.current = acao;
      // Toda falha de reconciliação depois deste ponto é aviso, não falha do POST.
      setAguardandoAtualizacao(true); setAberto(false);
      setMensagem({ tipo: "aviso", texto: "Solicitação aceita. Atualizando os detalhes..." });
      if (detalhe) {
        try { aplicar(detalhe); } catch { await atualizarDetalhes(); }
      } else await atualizarDetalhes();
      return true;
    } catch (excecao) {
      if (montado.current) setMensagem({ tipo: "erro", texto: excecao instanceof ErroApi
        ? excecao.status === 409 && excecao.codigo === "horario_indisponivel"
          ? "Esse horário acabou de ficar indisponível. Escolha uma nova opção entre os horários atualizados."
          : /^Erro \d+$/.test(excecao.message) ? "Não foi possível concluir a solicitação. Tente novamente." : excecao.message
        : "Não foi possível concluir a solicitação. Tente novamente." });
      return false;
    } finally { trava.current = false; if (montado.current) setProcessando(null); }
  }

  if (!dados) return <main className="flex min-h-screen flex-col items-center justify-center gap-4 px-6 text-center">
    <div ref={carga} tabIndex={-1}><p role={erroCarregar ? "alert" : "status"}>{erroCarregar?.texto ?? "Carregando..."}</p></div>
    {erroCarregar?.temporario && <button className={controleGestao} onClick={() => { carga.current?.focus(); setErroCarregar(null); setTentativa(n => n + 1); }}>Tentar novamente</button>}
  </main>;

  const { detalhe, fuso } = dados;
  const acoes = detalhe.acoes;
  return <main className="mx-auto flex min-h-screen max-w-md flex-col justify-center gap-4 px-6 py-12 text-gray-800 dark:text-neutral-200">
    <h1 ref={titulo} tabIndex={-1} className="text-lg font-semibold text-gray-900 dark:text-neutral-50">{detalhe.nomeNegocio}</h1>
    <div className="rounded-xl border border-gray-200 p-4 dark:border-neutral-700">
      {aguardandoAtualizacao ? <p>Detalhes aguardando atualização.</p> : <>
        <p>Status: {ROTULO_STATUS[detalhe.status] ?? detalhe.status}</p>
        <p className="mt-1">{formatarHorarioEstabelecimento(detalhe.inicio, fuso)}</p>
        {detalhe.profissional?.nome && <p>Profissional: {detalhe.profissional.nome}</p>}
        {detalhe.duracaoMinutos !== undefined && <p>Duração: {detalhe.duracaoMinutos} minutos</p>}
        {detalhe.servicosDetalhe?.length ? <ul>{detalhe.servicosDetalhe.map(s => <li key={s.servicoId}>{s.nome} · {s.duracaoMinutos} min · {formatarReais(s.preco)}</li>)}</ul> : <p>{detalhe.servicos.join(", ")}</p>}
        <p>{detalhe.local}</p><p className="mt-2 font-semibold">Total: {formatarReais(detalhe.total)}</p>
        {detalhe.regras?.limiteParaAlterarEm && <p className="mt-2 text-sm">Limite para alterações: {formatarHorarioEstabelecimento(detalhe.regras.limiteParaAlterarEm, fuso)}</p>}
      </>}
    </div>
    <p id="fuso-estabelecimento">Horários do estabelecimento ({fuso}).</p>
    {processando ? <p role="status">{processando === "cancelar" ? "Cancelando agendamento..." : processando === "remarcar" ? "Remarcando agendamento..." : "Atualizando detalhes..."}</p>
      : mensagem && <p role={mensagem.tipo === "erro" ? "alert" : "status"}>{mensagem.texto}</p>}
    {aguardandoAtualizacao ? <button className={controleGestao} disabled={!!processando} onClick={repetirAtualizacao}>{processando ? "Atualizando..." : "Atualizar detalhes"}</button> : <>
      {!acoes && <p role="status">As opções de alteração não estão disponíveis. Tente consultar novamente mais tarde.</p>}
      {acoes?.cancelar?.permitido === true ? <button className={`${controleGestao} text-red-700 dark:text-red-300`} disabled={!!processando} onClick={() => agir("cancelar")}>{processando === "cancelar" ? "Cancelando..." : "Cancelar agendamento"}</button>
        : acoes?.cancelar?.motivo && <p>Cancelamento: {acoes.cancelar.motivo}</p>}
      {acoes?.remarcar?.permitido === true ? <>
        <button ref={abrir} className={controleGestao} aria-expanded={aberto} aria-controls="remarcacao" disabled={!!processando} onClick={() => setAberto(v => !v)}>{aberto ? "Fechar remarcação" : "Remarcar agendamento"}</button>
        {aberto && <RemarcacaoAgendamento token={token} fuso={fuso} inicio={detalhe.inicio} bloqueado={!!processando} remarcando={processando === "remarcar"} confirmar={slot => agir("remarcar", slot)} />}
      </> : acoes?.remarcar?.motivo && <p>Remarcação: {acoes.remarcar.motivo}</p>}
    </>}
  </main>;
}
