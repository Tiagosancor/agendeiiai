"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeInput } from "@/components/estilos";
import { useAutenticacao } from "@/lib/auth-context";
import { erroWhatsApp } from "./ConfiguracoesWhatsApp";
import type { DetalheConversaWhatsApp, PaginaConversasWhatsApp } from "@/lib/tipos";
import "./painel-negocio.css";

const botao = `${classeBotaoPrimario} min-h-11`;
function horario(em: string) { return new Date(em).toLocaleString("pt-BR"); }
function avisarResumo() { window.dispatchEvent(new Event("whatsapp-conversas-alteradas")); }

export function ConversasWhatsApp({ id }: { id?: string }) {
  const { temPermissao } = useAutenticacao();
  if (!temPermissao("AtenderConversasWhatsApp")) return <p>Sem permissão para atender conversas do WhatsApp.</p>;
  return <div className="painel-cadastro painel-negocio space-y-4">
    <h1 className="text-xl font-semibold">Conversas do WhatsApp</h1>
    <Link href="/painel/whatsapp">Configurações e conexão</Link>
    {id ? <Detalhe key={id} id={id} /> : <Lista />}
  </div>;
}

function Lista() {
  const { chamarApi } = useAutenticacao();
  const [filtro, setFiltro] = useState("todas");
  const [pagina, setPagina] = useState(1);
  const [dados, setDados] = useState<PaginaConversasWhatsApp | null>(null);
  const [erro, setErro] = useState("");
  const [tentativa, setTentativa] = useState(0);
  const versao = useRef(0);
  const api = useRef(chamarApi);
  useEffect(() => { api.current = chamarApi; }, [chamarApi]);
  useEffect(() => {
    const atual = ++versao.current;
    const controle = new AbortController();
    api.current<PaginaConversasWhatsApp>(`/painel/whatsapp/conversas?filtro=${filtro}&pagina=${pagina}`, { sinal: controle.signal }).then(d => {
      if (atual === versao.current) { setDados(d); setErro(""); }
    }).catch(e => { if (atual === versao.current) setErro(erroWhatsApp(e)); });
    return () => {
      // Contador de requisições, não ref de DOM.
      // eslint-disable-next-line react-hooks/exhaustive-deps
      ++versao.current;
      controle.abort();
    };
  }, [filtro, pagina, tentativa]);
  function atualizar() { ++versao.current; setDados(null); setErro(""); setTentativa(t => t + 1); }
  return <section className="painel-negocio-secao space-y-4" aria-label="Lista de conversas">
    <label htmlFor="filtro-conversas">Mostrar</label>
    <select id="filtro-conversas" className={classeInput} value={filtro} onChange={e => { setFiltro(e.target.value); setPagina(1); atualizar(); }}>
      <option value="todas">Todas</option><option value="aguardando">Aguardando atendimento</option><option value="humano">Em atendimento humano</option>
    </select>
    <button className={botao} onClick={atualizar}>Atualizar conversas</button>
    {erro ? <p role="alert">{erro}</p> : !dados ? <p>Carregando conversas…</p> : <>
      <p>{dados.total} conversa(s)</p>
      <ul className="space-y-3">{dados.itens.map(c => <li key={c.id} className="rounded-lg border border-gray-200 p-3 dark:border-neutral-700">
        <Link className="block min-h-11 break-words font-semibold underline" href={`/painel/whatsapp/conversas/${encodeURIComponent(c.id)}`}>{c.clienteNome || c.telefone}</Link>
        {c.clienteNome && <p className="break-all">{c.telefone}</p>}
        <p>{c.aguardandoAtendimento ? "Aguardando atendimento" : c.status === "Humano" ? "Em atendimento humano" : "Com o agente"}</p>
        <p className="text-sm">Última mensagem: {horario(c.ultimaMensagemEm)}</p>
        {c.status === "Humano" && <p className="text-sm">{c.mensagensDesdeHumano} mensagem(ns) do cliente desde a transferência</p>}
      </li>)}</ul>
      {dados.itens.length === 0 && <p>Nenhuma conversa neste filtro.</p>}
      <nav className="flex flex-wrap items-center gap-3" aria-label="Páginas de conversas">
        <button className={botao} disabled={pagina <= 1} onClick={() => { setPagina(p => p - 1); atualizar(); }}>Anterior</button>
        <span>Página {dados.pagina} de {Math.max(1, Math.ceil(dados.total / dados.tamanhoPagina))}</span>
        <button className={botao} disabled={pagina * dados.tamanhoPagina >= dados.total} onClick={() => { setPagina(p => p + 1); atualizar(); }}>Próxima</button>
      </nav>
    </>}
  </section>;
}

function Detalhe({ id }: { id: string }) {
  const { chamarApi, temPermissao } = useAutenticacao();
  const router = useRouter();
  const [dados, setDados] = useState<DetalheConversaWhatsApp | null>(null);
  const [erro, setErro] = useState("");
  const [ocupado, setOcupado] = useState(false);
  const [confirmar, setConfirmar] = useState(false);
  const [tentativa, setTentativa] = useState(0);
  const versao = useRef(0);
  const operando = useRef(false);
  const api = useRef(chamarApi);
  useEffect(() => { api.current = chamarApi; }, [chamarApi]);
  useEffect(() => {
    const atual = ++versao.current;
    const controle = new AbortController();
    api.current<DetalheConversaWhatsApp>(`/painel/whatsapp/conversas/${encodeURIComponent(id)}`, { sinal: controle.signal }).then(d => {
      if (atual === versao.current) { setDados(d); setErro(""); }
    }).catch(e => { if (atual === versao.current) setErro(erroWhatsApp(e)); });
    return () => {
      // Invalida também as ações iniciadas depois da montagem (não é ref de DOM).
      // eslint-disable-next-line react-hooks/exhaustive-deps
      ++versao.current;
      controle.abort();
    };
  }, [id, tentativa]);
  async function executar(acao: "assumir" | "devolver" | "apagar" | "anteriores") {
    if (!dados || operando.current) return;
    operando.current = true;
    const atual = ++versao.current;
    setOcupado(true); setErro("");
    const base = `/painel/whatsapp/conversas/${encodeURIComponent(id)}`;
    try {
      if (acao === "anteriores") {
        const primeira = dados.mensagens[0];
        if (!primeira) return;
        const cursor = new URLSearchParams({ antesDe: primeira.em, antesDeId: primeira.id });
        const anteriores = await api.current<DetalheConversaWhatsApp>(`${base}?${cursor}`);
        if (atual === versao.current) setDados(d => d ? { ...d, haMaisAntigas: anteriores.haMaisAntigas, mensagens: [...anteriores.mensagens.filter(m => !d.mensagens.some(atual => atual.id === m.id)), ...d.mensagens] } : d);
      } else {
        await api.current<void>(`${base}/${acao}`, { metodo: "POST" });
        if (atual !== versao.current) return;
        avisarResumo();
        if (acao === "apagar") { router.push("/painel/whatsapp/conversas"); return; }
        // O 204 confirma a ação mesmo se a leitura seguinte falhar.
        setDados(d => d ? { ...d, status: acao === "assumir" ? "Humano" : "Agente", aguardandoAtendimento: false, mensagensDesdeHumano: 0, humanoDesde: null, humanoAte: null, humanoMotivo: null } : d);
        try {
          const atualizado = await api.current<DetalheConversaWhatsApp>(base);
          if (atual === versao.current) setDados(atualizado);
        } catch {
          if (atual === versao.current) setErro("Ação feita; não foi possível atualizar a conversa. Use Atualizar mensagens para tentar novamente.");
        }
      }
    } catch (e) { if (atual === versao.current) setErro(erroWhatsApp(e)); }
    finally { operando.current = false; if (atual === versao.current) { setOcupado(false); setConfirmar(false); } }
  }
  return <section className="painel-negocio-secao space-y-4" aria-label="Detalhe da conversa">
    <Link href="/painel/whatsapp/conversas">Voltar às conversas</Link>
    {erro && <p role="alert">{erro}</p>}
    {!dados ? <><p>{erro ? "Conversa indisponível." : "Carregando conversa…"}</p><button className={botao} onClick={() => { setErro(""); setTentativa(t => t + 1); }}>Tentar novamente</button></> : <>
      <h2 className="break-words font-semibold">{dados.clienteNome || dados.telefone}</h2>
      {dados.clienteNome && <p className="break-all">{dados.telefone}</p>}
      <p>{dados.aguardandoAtendimento ? "Aguardando atendimento" : dados.status === "Humano" ? "Em atendimento humano" : "Com o agente"}</p>
      {dados.status === "Humano" && <p className="text-sm">{dados.mensagensDesdeHumano} mensagem(ns) do cliente desde a transferência{dados.humanoDesde && ` em ${horario(dados.humanoDesde)}`}{dados.humanoAte && `. Devolução automática após ${horario(dados.humanoAte)}`}</p>}
      <p className="text-sm">Responda ao cliente pelo WhatsApp conectado. Ao devolver, o agente responderá à próxima mensagem nova.</p>
      <div className="flex flex-wrap gap-3">
        <button className={botao} disabled={ocupado || (dados.status === "Humano" && !dados.aguardandoAtendimento)} onClick={() => executar("assumir")}>Assumir conversa</button>
        <button className={botao} disabled={ocupado || dados.status !== "Humano"} onClick={() => executar("devolver")}>Devolver ao agente</button>
        <button className={botao} disabled={ocupado} onClick={() => { ++versao.current; setDados(null); setTentativa(t => t + 1); }}>Atualizar mensagens</button>
        {temPermissao("GerenciarClientes") && <button className={botao} disabled={ocupado} onClick={() => setConfirmar(true)}>Apagar conversa</button>}
      </div>
      {ocupado && <p role="status">Aguarde…</p>}
      {dados.haMaisAntigas && <button className={botao} disabled={ocupado || !dados.mensagens.length} onClick={() => executar("anteriores")}>Carregar anteriores</button>}
      <ol className="space-y-3" aria-label="Mensagens da conversa">{dados.mensagens.map(m => <li key={m.id} className={m.papel === "Sistema" ? "break-words whitespace-pre-wrap px-3 py-2 text-sm text-gray-500 dark:text-neutral-400" : `break-words whitespace-pre-wrap rounded-lg border border-gray-200 p-3 dark:border-neutral-700 ${m.papel === "Assistente" ? "sm:ml-8" : "sm:mr-8"}`}>
        <p className="text-xs font-semibold">{m.papel} · <time dateTime={m.em}>{horario(m.em)}</time></p><p>{m.texto}</p>
      </li>)}</ol>
      {!dados.mensagens.length && <p>Sem mensagens disponíveis.</p>}
    </>}
    <Modal aberto={confirmar} titulo="Apagar conversa?" aoFechar={() => { if (!operando.current) setConfirmar(false); }}>
      <p>As mensagens serão apagadas e a conversa será anonimizada. Esta ação não pode ser desfeita.</p>
      <div className="mt-4 flex flex-wrap gap-3"><button className={botao} disabled={ocupado} onClick={() => setConfirmar(false)}>Cancelar</button><button className={botao} disabled={ocupado} onClick={() => executar("apagar")}>Confirmar exclusão</button></div>
    </Modal>
  </section>;
}
