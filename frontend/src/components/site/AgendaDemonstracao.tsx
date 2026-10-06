"use client";

import { useRef, useState, type FormEvent, type KeyboardEvent, type ReactNode } from "react";
import "./agenda-demonstracao.css";

const MODULOS: { nome: string; icone: ReactNode }[] = [
  { nome: "Agenda", icone: <><rect x="4" y="5" width="16" height="16" rx="3" /><path d="M8 3v4m8-4v4M4 11h16m-11 4h6" /></> },
  { nome: "Usuários", icone: <><circle cx="9" cy="8" r="3" /><path d="M3 21v-3a6 6 0 0 1 12 0v3m1-16a3 3 0 0 1 0 6m2 4a5 5 0 0 1 3 5" /></> },
  { nome: "Profissionais", icone: <><circle cx="12" cy="7" r="4" /><path d="M4 21v-2a8 8 0 0 1 16 0v2" /></> },
  { nome: "Serviços", icone: <><rect x="3" y="7" width="18" height="14" rx="3" /><path d="M8 7V3h8v4M3 12h18m-11 0v3h4v-3" /></> },
  { nome: "Cupons", icone: <><path d="M3 3h9l9 9-9 9-9-9Z" /><circle cx="8" cy="8" r="1" /></> },
  { nome: "Clientes", icone: <><circle cx="12" cy="8" r="3" /><path d="M5 21v-2a7 7 0 0 1 14 0v2" /></> },
  { nome: "Financeiro", icone: <><path d="M4 21h16M6 17v-5m6 5V8m6 9V4M4 8l6-4 4 2 6-4" /></> },
  { nome: "Fidelidade", icone: <><rect x="3" y="8" width="18" height="13" rx="2" /><path d="M12 8v13M3 12h18m-6-4H7a3 3 0 1 1 3-3Zm0 0h5a3 3 0 1 0-3-3Z" /></> },
  { nome: "Meu negócio", icone: <><path d="M3 10 5 3h14l2 7M5 10v11h14V10M9 21v-7h6v7" /><path d="M3 10a3 3 0 0 0 6 0 3 3 0 0 0 6 0 3 3 0 0 0 6 0" /></> },
];

const PROFISSIONAIS = ["Diego Lima", "Mariana Alves", "Carlos Santos"];
const SERVICOS = [
  { nome: "Corte masculino", valor: "R$ 45,00", minutos: 30 },
  { nome: "Barba completa", valor: "R$ 35,00", minutos: 20 },
];
type Status = "Agendado" | "Concluído" | "Faltou" | "Cancelado";
type Atendimento = { id: number; profissional: string; data: string; horario: string; cliente: string; servico: string; valor: string; status: Status };
const ORIGINAIS: Atendimento[] = [
  { id: 1, profissional: "Diego Lima", data: "2026-09-25", horario: "09:30–10:00", cliente: "Pedro Costa", servico: "Corte masculino", valor: "R$ 45,00", status: "Agendado" },
  { id: 2, profissional: "Diego Lima", data: "2026-09-25", horario: "13:00–13:20", cliente: "André Rocha", servico: "Barba completa", valor: "R$ 35,00", status: "Agendado" },
  { id: 3, profissional: "Mariana Alves", data: "2026-09-25", horario: "10:00–10:30", cliente: "Pedro Costa", servico: "Corte masculino", valor: "R$ 45,00", status: "Agendado" },
  { id: 4, profissional: "Carlos Santos", data: "2026-09-25", horario: "11:00–11:20", cliente: "André Rocha", servico: "Barba completa", valor: "R$ 35,00", status: "Agendado" },
  { id: 5, profissional: "Diego Lima", data: "2026-09-26", horario: "10:30–11:00", cliente: "André Rocha", servico: "Corte masculino", valor: "R$ 45,00", status: "Agendado" },
];

/** Simulação de marketing isolada: somente memória React, sem API ou persistência. */
export function AgendaDemonstracao({ nomeMarca }: { nomeMarca: string }) {
  const [profissional, definirProfissional] = useState(PROFISSIONAIS[0]);
  const [data, definirData] = useState("2026-09-25");
  const [atendimentos, definirAtendimentos] = useState(ORIGINAIS);
  const [feedback, definirFeedback] = useState("");
  const dialogo = useRef<HTMLDialogElement>(null);
  const botaoNovo = useRef<HTMLButtonElement>(null);
  const proximoId = useRef(6);
  const lista = atendimentos.filter(item => item.profissional === profissional && item.data === data);
  const horarios = ["15:00", "16:00", "17:00"].filter(hora => !lista.some(item => item.horario.startsWith(hora) && item.status !== "Cancelado"));

  function reiniciar() {
    definirProfissional(PROFISSIONAIS[0]); definirData("2026-09-25"); definirAtendimentos(ORIGINAIS);
    proximoId.current = 6; definirFeedback("Demonstração reiniciada");
  }
  function mudarStatus(id: number, status: Status) {
    definirAtendimentos(atual => atual.map(item => item.id === id ? { ...item, status } : item));
    definirFeedback(`Status atualizado: ${status}`);
  }
  function abrir() {
    const modal = dialogo.current!;
    modal.querySelector("form")!.reset();
    modal.showModal();
    // Centraliza no painel quando cabe na viewport; mantém o formulário acessível em mobile.
    const painel = modal.closest("figure")!.getBoundingClientRect();
    modal.style.left = `${Math.max(16, Math.min(innerWidth - modal.offsetWidth - 16, painel.left + (painel.width - modal.offsetWidth) / 2))}px`;
    modal.style.top = `${Math.max(16, Math.min(innerHeight - modal.offsetHeight - 16, painel.top + (painel.height - modal.offsetHeight) / 2))}px`;
  }
  function limitarFoco(evento: KeyboardEvent<HTMLDialogElement>) {
    if (evento.key !== "Tab") return;
    const controles = Array.from(evento.currentTarget.querySelectorAll<HTMLElement>("select:not([disabled]), button:not([disabled])"));
    const primeiro = controles[0];
    const ultimo = controles[controles.length - 1];
    if (evento.shiftKey && document.activeElement === primeiro) {
      evento.preventDefault(); ultimo.focus();
    } else if (!evento.shiftKey && document.activeElement === ultimo) {
      evento.preventDefault(); primeiro.focus();
    }
  }
  function criar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const campos = new FormData(evento.currentTarget);
    const servico = SERVICOS[Number(campos.get("servico"))];
    const hora = String(campos.get("horario"));
    const [h, m] = hora.split(":").map(Number);
    const fim = h * 60 + m + servico.minutos;
    const horario = `${hora}–${String(Math.floor(fim / 60)).padStart(2, "0")}:${String(fim % 60).padStart(2, "0")}`;
    const id = proximoId.current++;
    definirAtendimentos(atual => [...atual, { id, profissional, data, horario, cliente: String(campos.get("cliente")), servico: servico.nome, valor: servico.valor, status: "Agendado" }]);
    definirFeedback("Agendamento criado na demonstração"); dialogo.current!.close();
  }

  return <figure className="site-painel site-agenda-demo" aria-label="Demonstração visual da Agenda">
    <figcaption className="sr-only">Demonstração interativa com dados fictícios. Nenhuma alteração é salva ou enviada ao produto real.</figcaption>
    <div className="agenda-demo-sidebar">
      <div className="agenda-demo-marca"><img src="/brand/calendario-confirmado.svg" alt="" width={26} height={26} /><strong>{nomeMarca}</strong></div>
      <ul aria-label="Módulos demonstrados">{MODULOS.map(({ nome, icone }) => <li key={nome} className={nome === "Agenda" ? "agenda-demo-ativo" : ""} title={`${nome} — representação visual, sem navegação`}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{icone}</svg>
        <span className="agenda-demo-modulo">{nome}</span><span className="sr-only">{nome === "Agenda" ? ", selecionada" : ""}</span>
      </li>)}</ul>
    </div>
    <div className="agenda-demo-conteudo">
      <div className="agenda-demo-cabecalho"><h3>Agenda</h3><span className="agenda-demo-pill">● Demonstração interativa</span></div>
      <div className="agenda-demo-filtros">
        <label>Profissional<select className="agenda-demo-campo" value={profissional} onChange={e => { definirProfissional(e.target.value); definirFeedback(""); }}>{PROFISSIONAIS.map(nome => <option key={nome}>{nome}</option>)}</select></label>
        <label>Data<input className="agenda-demo-campo" type="date" value={data} required onChange={e => { definirData(e.target.value); definirFeedback(""); }} /></label>
        <button ref={botaoNovo} type="button" className="agenda-demo-novo" disabled={!data} onClick={abrir}>Novo agendamento</button>
      </div>
      <div className="agenda-demo-utilitarios"><small>Experimente 25 ou 26/09/2026</small><button type="button" onClick={reiniciar}>Reiniciar demo</button></div>
      <ul key={`${profissional}-${data}`} className="agenda-demo-agendamentos">{lista.length ? lista.map(item => <li key={item.id}>
        <div className="agenda-demo-atendimento"><div className="agenda-demo-horario"><strong>{item.horario}</strong><span key={item.status} data-status={item.status}>{item.status}</span></div><p>{item.cliente}</p><small>{item.servico} · {item.valor}</small></div>
        <div className="agenda-demo-acoes" role="group" aria-label={`Ações de ${item.cliente}, ${item.horario}`}>
          <button type="button" onClick={() => mudarStatus(item.id, "Concluído")}>Concluir</button><button type="button" onClick={() => mudarStatus(item.id, "Faltou")}>Faltou</button><button type="button" onClick={() => mudarStatus(item.id, "Cancelado")}>Cancelar</button>
        </div>
      </li>) : <li className="agenda-demo-vazio">Nenhum agendamento para esta data</li>}</ul>
      <p className="agenda-demo-feedback" role="status">{feedback}</p>
    </div>
    <dialog ref={dialogo} className="agenda-demo-modal" aria-labelledby="agenda-demo-modal-titulo" aria-describedby="agenda-demo-modal-descricao" onKeyDown={limitarFoco} onClose={() => botaoNovo.current?.focus({ preventScroll: true })}>
      <form onSubmit={criar}>
        <h4 id="agenda-demo-modal-titulo">Novo agendamento</h4>
        <p id="agenda-demo-modal-descricao">Simulação para {profissional}. Nada será salvo no produto real.</p>
        <label>Cliente<select name="cliente" required><option>Pedro Costa</option><option>André Rocha</option></select></label>
        <label>Serviço<select name="servico" required>{SERVICOS.map((s, i) => <option key={s.nome} value={i}>{s.nome} · {s.valor}</option>)}</select></label>
        <label>Horário<select name="horario" required>{horarios.map(hora => <option key={hora}>{hora}</option>)}</select></label>
        {!horarios.length && <p>Todos os horários de exemplo foram utilizados. Reinicie a demo ou escolha outra data.</p>}
        <div className="agenda-demo-modal-acoes"><button type="button" onClick={() => dialogo.current!.close()}>Cancelar</button><button className="agenda-demo-novo" disabled={!horarios.length} type="submit">Criar agendamento</button></div>
      </form>
    </dialog>
  </figure>;
}
