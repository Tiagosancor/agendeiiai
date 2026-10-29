"use client";

import { useEffect, useRef, useState } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { ErroApi } from "@/lib/api";
import type { StatusWhatsApp, UsoIaMes } from "@/lib/tipos";
import { classeBotaoPrimario, classeInput } from "@/components/estilos";

export function erroWhatsApp(e: unknown) {
  return e instanceof ErroApi ? typeof e.corpo?.title === "string" ? e.corpo.title : e.message : "Não foi possível concluir. Tente novamente.";
}

export function ConfiguracoesWhatsApp({ mensagem, status }: { mensagem: string | null; status: StatusWhatsApp }) {
  const { chamarApi } = useAutenticacao();
  const [texto, setTexto] = useState(mensagem ?? "");
  const [ocupado, setOcupado] = useState(false);
  const [aviso, setAviso] = useState("");
  const [falhou, setFalhou] = useState(false);
  const versao = useRef(0);
  const operando = useRef(false);
  useEffect(() => () => { ++versao.current; }, []);
  return <section className="painel-negocio-secao space-y-4" aria-label="Configurações do agente">
    <h2 className="font-semibold">Configurações do agente</h2>
    <dl className="grid gap-3 sm:grid-cols-3">{[["IA", status.iaAtiva], ["Agendamento", status.agendamentoAtivo], ["Atendimento humano pelo agente", status.atendimentoHumanoAtivo]].map(([nome, ativa]) => <div key={String(nome)}><dt>{nome}</dt><dd>{ativa ? "Ativo" : "Desligado"}</dd></div>)}</dl>
    <p className="text-sm">Estados definidos pelo serviço, somente leitura.</p>
    <p>Gestão de agendamentos: {status.gestaoModo}</p>
    <form className="space-y-3" onSubmit={async e => {
      e.preventDefault();
      if (operando.current) return;
      operando.current = true;
      const atual = ++versao.current;
      setOcupado(true); setAviso(""); setFalhou(false);
      try {
        await chamarApi<void>("/painel/whatsapp/mensagem-transferencia", { metodo: "PUT", corpo: { mensagem: texto.trim() || null } });
        if (atual === versao.current) { setTexto(texto.trim()); setAviso("Mensagem salva."); }
      } catch (erro) { if (atual === versao.current) { setFalhou(true); setAviso(erroWhatsApp(erro)); } }
      finally { operando.current = false; if (atual === versao.current) setOcupado(false); }
    }}>
      <label className="block" htmlFor="mensagem-transferencia">Mensagem de transferência</label>
      <textarea id="mensagem-transferencia" className={classeInput} maxLength={300} rows={4} value={texto} disabled={ocupado} onChange={e => setTexto(e.target.value)} aria-describedby="limite-transferencia" />
      <p id="limite-transferencia" className="text-sm">{texto.length}/300 caracteres. Deixe vazio para remover a mensagem adicional.</p>
      <button disabled={ocupado} className={classeBotaoPrimario}>{ocupado ? "Salvando…" : "Salvar mensagem"}</button>
      {aviso && <p role={falhou ? "alert" : "status"}>{aviso}</p>}
    </form>
  </section>;
}

export function UsoWhatsApp() {
  const { chamarApi } = useAutenticacao();
  // O primeiro GET usa o mês UTC corrente escolhido pelo servidor.
  const [mes, setMes] = useState("");
  const [dados, setDados] = useState<UsoIaMes | null>(null);
  const [erro, setErro] = useState("");
  const [carregando, setCarregando] = useState(true);
  const [tentativa, setTentativa] = useState(0);
  const versao = useRef(0);
  const api = useRef(chamarApi);
  useEffect(() => { api.current = chamarApi; }, [chamarApi]);
  useEffect(() => {
    const atual = ++versao.current;
    const controle = new AbortController();
    api.current<UsoIaMes>(`/painel/whatsapp/uso${mes ? `?mes=${mes}` : ""}`, { sinal: controle.signal }).then(d => {
      if (atual === versao.current) { setDados(d); setErro(""); setCarregando(false); }
    }).catch(e => { if (atual === versao.current) { setErro(erroWhatsApp(e)); setCarregando(false); } });
    return () => {
      // Contador de requisições, não ref de DOM.
      // eslint-disable-next-line react-hooks/exhaustive-deps
      ++versao.current;
      controle.abort();
    };
  }, [mes, tentativa]);
  return <section className="painel-negocio-secao space-y-4" aria-label="Uso de IA">
    <h2 className="font-semibold">Uso do mês</h2>
    <label className="block" htmlFor="mes-uso">Mês (UTC) — formato AAAA-MM</label>
    <input id="mes-uso" type="month" min="2020-01" max="9999-11" className={classeInput} value={mes || dados?.mes || ""} onChange={e => {
      const valor = e.target.value;
      if (/^\d{4}-\d{2}$/.test(valor) && valor >= "2020-01" && valor <= "9999-11" && Number(valor.slice(5)) <= 12 && Number(valor.slice(5)) >= 1) {
        ++versao.current;
        setCarregando(true); setDados(null); setErro(""); setMes(valor);
      }
    }} />
    {carregando && <p>Carregando uso…</p>}
    {erro && <p role="alert">{erro} <button className={classeBotaoPrimario} onClick={() => { ++versao.current; setErro(""); setCarregando(true); setTentativa(t => t + 1); }}>Tentar novamente</button></p>}
    {dados && <>
      <p>{(dados.tokensEntrada + dados.tokensSaida).toLocaleString("pt-BR")} de {dados.cotaMensalTokens.toLocaleString("pt-BR")} tokens · {dados.percentualCota.toLocaleString("pt-BR")}% da cota</p>
      <progress className="w-full" aria-label="Cota mensal de tokens utilizada" max={100} value={Math.min(100, Math.max(0, dados.percentualCota))} />
      <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{[
        ["Chamadas", dados.chamadas], ["Chamadas com falha", dados.chamadasComFalha], ["Tokens de entrada", dados.tokensEntrada], ["Tokens de saída", dados.tokensSaida],
        ["Chamadas hoje (UTC)", `${dados.chamadasHoje} / ${dados.limiteChamadasPorDia}`], ["Conversas atendidas", dados.conversasAtendidas], ["Respostas de fallback", dados.respostasFallback],
        ["Agendamentos confirmados", dados.agendamentosConfirmados], ["Cancelamentos", dados.cancelamentos], ["Remarcações", dados.remarcacoes], ["Transferências para humano", dados.transferenciasHumano],
      ].map(([nome, valor]) => <div key={nome}><dt className="text-sm">{nome}</dt><dd className="font-semibold">{valor}</dd></div>)}</dl>
      <div className="overflow-x-auto"><table className="w-full text-left text-sm"><caption className="text-left font-semibold">Uso por dia (UTC)</caption><thead><tr><th scope="col">Dia</th><th scope="col">Chamadas</th><th scope="col">Tokens</th></tr></thead><tbody>{dados.porDia.map(d => <tr key={d.dia}><th scope="row">{d.dia}</th><td>{d.chamadas}</td><td>{d.tokens.toLocaleString("pt-BR")}</td></tr>)}</tbody></table></div>
      {dados.porDia.length === 0 && <p>Sem uso neste mês.</p>}
    </>}
  </section>;
}
