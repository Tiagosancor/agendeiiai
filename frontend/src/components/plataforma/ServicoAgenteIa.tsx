"use client";

import { useEffect, useRef, useState } from "react";
import { ErroApi } from "@/lib/api";
import type { DefinirAgenteIaRequisicao, HistoricoAgenteIa, NegocioNaPlataforma, OrigemAgenteIa, ServicoAgenteIaNegocio } from "@/lib/tipos";
import { classeBotaoPrimario, classeBotaoSecundario, classeInput, classeLabel } from "@/components/estilos";
import { usePlataforma } from "./ContextoPlataforma";

function textoErro(erro: unknown) {
  return erro instanceof ErroApi ? erro.message : "Não foi possível concluir a operação. Tente novamente.";
}

const ROTULOS_ORIGEM: Record<OrigemAgenteIa, string> = { Manual: "Contratação manual", Plano: "Incluído no plano", Adicional: "Serviço adicional" };

export function ControleServicoAgenteIa({ negocio, aoAlterar }: { negocio: NegocioNaPlataforma; aoAlterar: (servico: ServicoAgenteIaNegocio) => Promise<void> }) {
  const { chamar } = usePlataforma();
  const [confirmando, setConfirmando] = useState(false);
  const [motivo, setMotivo] = useState("");
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [resultado, setResultado] = useState<ServicoAgenteIaNegocio | null>(null);
  const trava = useRef(false);
  const botao = useRef<HTMLButtonElement>(null);
  const campoMotivo = useRef<HTMLTextAreaElement>(null);
  const devolverFoco = useRef(false);
  const contratado = resultado?.contratado ?? negocio.agenteIaContratado;
  const origem = resultado ? resultado.origem : negocio.agenteIaOrigem;

  useEffect(() => {
    if (confirmando) campoMotivo.current?.focus();
    else if (!salvando && devolverFoco.current) {
      botao.current?.focus();
      devolverFoco.current = false;
    }
  }, [confirmando, salvando]);

  async function salvar() {
    if (trava.current) return;
    trava.current = true;
    setSalvando(true); setErro(null);
    let novo: ServicoAgenteIaNegocio;
    try {
      const corpo: DefinirAgenteIaRequisicao = { contratado: !contratado, ...(motivo.trim() ? { motivo: motivo.trim() } : {}) };
      novo = await chamar<ServicoAgenteIaNegocio>(`/plataforma/negocios/${negocio.id}/agente-ia`, { metodo: "PUT", corpo });
      setResultado(novo); setConfirmando(false); setMotivo("");
    } catch (e) {
      setErro(textoErro(e));
      return;
    } finally {
      trava.current = false; setSalvando(false);
    }
    // A lista trata sua falha de leitura; o PUT já foi concluído.
    await aoAlterar(novo);
  }

  return <div className="min-w-48 space-y-2">
    <p>{contratado ? "Ligado" : "Desligado"}{origem ? ` · Origem: ${ROTULOS_ORIGEM[origem]}` : ""}</p>
    <p className="text-xs text-gray-500 dark:text-neutral-400">WhatsApp {negocio.whatsAppConectado ? "conectado" : "não conectado"}</p>
    <button ref={botao} type="button" role="switch" aria-label={`Agente de IA — ${negocio.nome}`} aria-checked={contratado} disabled={salvando || confirmando} className={`${classeBotaoSecundario} min-h-11`} onClick={() => { devolverFoco.current = true; setConfirmando(true); setErro(null); }}>
      {contratado ? "Desligar" : "Ligar"}
    </button>
    {confirmando && <form className="space-y-2" onSubmit={e => { e.preventDefault(); void salvar(); }}>
      <p>Confirmar {contratado ? "desligamento" : "ativação"} do serviço para {negocio.nome}?</p>
      <label className="block">
        <span className={classeLabel}>Motivo (opcional)</span>
        <textarea ref={campoMotivo} className={classeInput} maxLength={500} value={motivo} disabled={salvando} onChange={e => setMotivo(e.target.value)} />
      </label>
      <div className="flex flex-wrap gap-2">
        <button type="submit" disabled={salvando} className={`${classeBotaoPrimario} min-h-11`}>{salvando ? "Salvando…" : "Confirmar"}</button>
        <button type="button" disabled={salvando} className={`${classeBotaoSecundario} min-h-11`} onClick={() => { setConfirmando(false); setErro(null); }}>Cancelar</button>
      </div>
    </form>}
    {erro && <p role="alert" className="text-sm text-red-700 dark:text-red-400">{erro}</p>}
  </div>;
}

export function HistoricoServicoAgenteIa({ negocioId, servico }: { negocioId: string; servico: ServicoAgenteIaNegocio }) {
  const { chamar } = usePlataforma();
  const [historico, setHistorico] = useState<HistoricoAgenteIa[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [tentativa, setTentativa] = useState(0);
  useEffect(() => {
    const controle = new AbortController();
    chamar<HistoricoAgenteIa[]>(`/plataforma/negocios/${negocioId}/agente-ia/historico`, { sinal: controle.signal })
      .then(dados => { if (!controle.signal.aborted) { setHistorico(dados); setErro(null); } })
      .catch(e => { if (!controle.signal.aborted) setErro(textoErro(e)); });
    return () => controle.abort();
  }, [chamar, negocioId, servico.alteradoEm, servico.contratado, tentativa]);

  return <section aria-label="Histórico do agente de IA" className="space-y-2 rounded-lg border border-gray-200 p-3 dark:border-neutral-800">
    <h3 className="font-semibold">Histórico do agente de IA</h3>
    <p>Serviço {servico.contratado ? "ligado" : "desligado"}{servico.origem ? ` · Origem: ${ROTULOS_ORIGEM[servico.origem]}` : ""}</p>
    {servico.alteradoEm && <p className="text-sm break-words">Alterado em {new Date(servico.alteradoEm).toLocaleString("pt-BR")} por {servico.alteradoPor ?? "—"}</p>}
    {erro ? <p role="alert">{erro} <button type="button" className={classeBotaoSecundario} onClick={() => setTentativa(t => t + 1)}>Tentar novamente</button></p> : historico === null ? <p>Carregando histórico…</p> : historico.length === 0 ? <p>Nenhuma alteração registrada.</p> : <ul className="space-y-2 text-sm break-words">
      {historico.map((item, indice) => <li key={`${item.em}-${indice}`}>
        {new Date(item.em).toLocaleString("pt-BR")} — {item.acao === "LigarAgenteIa" ? "Ligado" : "Desligado"} · {item.autor}
        {item.detalhes && <p className="whitespace-pre-wrap">{item.detalhes}</p>}
      </li>)}
    </ul>}
  </section>;
}
