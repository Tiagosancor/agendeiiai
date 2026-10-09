"use client";

import { useEffect, useRef, useState } from "react";
import { ErroApi, requisicaoApiPublica } from "@/lib/api";
import type { HorariosRemarcacaoPublica } from "@/lib/tipos";
import { formatarHorarioEstabelecimento } from "@/lib/horario-estabelecimento";
import { formatarDiaDaFaixa, proximosDiasDoNegocio } from "./datas-agendamento";

export const controleGestao = "gestao-controle";

export function RemarcacaoAgendamento({ token, fuso, inicio, bloqueado, remarcando, confirmar }: {
  token: string; fuso: string; inicio: string; bloqueado: boolean; remarcando: boolean;
  confirmar: (slot: string) => Promise<boolean>;
}) {
  const formatoData = new Intl.DateTimeFormat("en-CA", { timeZone: fuso, year: "numeric", month: "2-digit", day: "2-digit" });
  const [data, setData] = useState(() => formatoData.format(new Date(inicio)));
  const [resultado, setResultado] = useState<{ data: string; slots: string[]; fuso: string } | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [tentativa, setTentativa] = useState(0);
  const [slot, setSlot] = useState<string | null>(null);
  const [revisando, setRevisando] = useState(false);
  const geracao = useRef(0);
  const enviando = useRef(false);
  const campo = useRef<HTMLInputElement>(null);
  const titulo = useRef<HTMLHeadingElement>(null);

  useEffect(() => { campo.current?.focus(); }, []);
  useEffect(() => {
    if (!data) return;
    const atual = ++geracao.current;
    let vigente = true;
    requisicaoApiPublica<HorariosRemarcacaoPublica>(`/meus-agendamentos/${token}/horarios-livres?data=${encodeURIComponent(data)}`)
      .then(resposta => {
        if (!vigente || geracao.current !== atual) return;
        if (resposta.data !== data || !Array.isArray(resposta.horarios)) throw new Error("Resposta de disponibilidade inválida");
        // O fuso é parte do contrato desta consulta, sem inferência pelo aparelho.
        new Intl.DateTimeFormat("pt-BR", { timeZone: resposta.fuso }).format();
        if (!resposta.fuso) throw new Error("Fuso ausente");
        resposta.horarios.forEach(iso => formatarHorarioEstabelecimento(iso, resposta.fuso));
        setResultado({ data, slots: resposta.horarios, fuso: resposta.fuso });
      }).catch(excecao => {
        if (vigente && geracao.current === atual) setErro(excecao instanceof ErroApi && excecao.codigo
          ? excecao.message : "Não foi possível consultar os horários. Tente novamente.");
      });
    return () => { vigente = false; };
  }, [token, data, tentativa]);

  function invalidar() {
    geracao.current++;
    setResultado(null); setSlot(null); setRevisando(false); setErro(null);
  }
  function repetir() {
    titulo.current?.focus();
    invalidar(); setTentativa(n => n + 1);
  }
  async function enviar(evento: React.FormEvent) {
    evento.preventDefault();
    if (enviando.current || !slot || bloqueado || !resultado || resultado.data !== data) return;
    enviando.current = true;
    try {
      const aceito = await confirmar(slot);
      if (!aceito) repetir(); // Reconsulta real após conflito/erro; nunca mistura opções antigas.
    } finally { enviando.current = false; }
  }
  const carregando = !!data && !resultado && !erro;
  const hora = (iso: string) => new Intl.DateTimeFormat("pt-BR", { timeZone: resultado?.fuso ?? fuso, hour: "2-digit", minute: "2-digit" }).format(new Date(iso));
  const datasProximas = proximosDiasDoNegocio(7, fuso);
  const hoje = datasProximas[0];
  const mesVisivel = data && new Intl.DateTimeFormat("pt-BR", { month: "long", year: "numeric", timeZone: "UTC" })
    .format(new Date(`${data}T12:00:00Z`));

  return <section id="remarcacao" aria-labelledby="titulo-remarcacao" className="gestao-remarcacao">
    <span className="gestao-etapa">{revisando ? "02 · Revise seu novo horário" : "01 · Escolha seu novo horário"}</span>
    <h2 id="titulo-remarcacao" ref={titulo} tabIndex={-1}>{revisando ? "Confirme a remarcação" : "Escolha uma nova data e horário"}</h2>
    {!revisando ? <>
      <p className="gestao-mes-visivel" aria-live="polite">{mesVisivel}</p>
      <div className="gestao-faixa-datas" role="group" aria-label="Próximas datas">
        {datasProximas.map(dia => {
          const rotuloCompleto = new Intl.DateTimeFormat("pt-BR", { dateStyle: "full", timeZone: "UTC" }).format(new Date(`${dia}T12:00:00Z`));
          return <button key={dia} type="button" className={`${controleGestao} gestao-dia`} aria-pressed={data === dia} aria-label={dia === hoje ? `Hoje, ${rotuloCompleto}` : rotuloCompleto} disabled={bloqueado} onClick={() => { invalidar(); setData(dia); }}><span>{dia === hoje ? "Hoje" : formatarDiaDaFaixa(dia)}</span><strong>{dia.slice(8)}</strong></button>;
        })}
      </div>
      <label htmlFor="data-remarcacao" className="block">Data da remarcação</label>
      <input ref={campo} id="data-remarcacao" type="date" value={data} disabled={bloqueado} aria-describedby="fuso-estabelecimento" className={controleGestao} onChange={e => { invalidar(); setData(e.target.value); }} />
      {carregando && <p role="status">Consultando horários...</p>}
      {erro && <p role="alert">{erro}</p>}
      {erro && <button className={controleGestao} onClick={repetir} disabled={bloqueado}>Tentar novamente horários</button>}
      {resultado && resultado.slots.length === 0 && <p role="status">Nenhum horário disponível para esta data. Escolha outra data.</p>}
      {resultado && <fieldset disabled={bloqueado}>
        <legend className="mb-2">Horários disponíveis</legend>
        <div className="gestao-slots">
          {resultado.slots.map(iso => <button key={iso} type="button" aria-label={hora(iso)} aria-pressed={slot === iso} className={`${controleGestao} gestao-slot`} onClick={() => setSlot(iso)}>{hora(iso)}</button>)}
        </div>
      </fieldset>}
      <button className={`${controleGestao} gestao-primario`} disabled={!slot || bloqueado} onClick={() => { setRevisando(true); requestAnimationFrame(() => titulo.current?.focus()); }}>Revisar remarcação</button>
    </> : <form onSubmit={enviar} className="gestao-revisao">
      <div className="gestao-comparacao" aria-label="Comparação entre o horário atual e o novo horário">
        <div className="gestao-review-periodo"><strong>DE</strong><p>Horário atual: {formatarHorarioEstabelecimento(inicio, fuso)}</p></div>
        <span className="gestao-comparacao-seta" aria-hidden="true">→</span>
        <div className="gestao-review-periodo" data-destino="true"><strong>PARA</strong><p>Novo horário: {formatarHorarioEstabelecimento(slot!, resultado!.fuso)}</p></div>
      </div>
      <button type="button" disabled={bloqueado} className={controleGestao} onClick={() => { setRevisando(false); requestAnimationFrame(() => campo.current?.focus()); }}>Voltar à seleção</button>
      <button type="submit" disabled={bloqueado} className={`${controleGestao} gestao-primario`}>{remarcando ? "Remarcando..." : "Confirmar novo horário"}</button>
    </form>}
  </section>;
}
