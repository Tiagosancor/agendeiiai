"use client";

import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { classeBotaoPrimario, classeBotaoSecundario, classeInput, classeLabel } from "@/components/estilos";
import { ErroApi, useAutenticacao } from "@/lib/auth-context";
import type { ClienteBusca } from "@/lib/tipos";
import { criarClientesAgendaService } from "@/services/clientes-agenda-service";

interface Props {
  cliente: ClienteBusca | null;
  aoSelecionar: (cliente: ClienteBusca | null) => void;
}

function formatarTelefone(telefone: string | null): string {
  if (!telefone) return "Sem telefone";
  let digitos = telefone.replace(/\D/g, "");
  if (digitos.startsWith("55") && digitos.length > 11) digitos = digitos.slice(2);
  if (digitos.length === 11) return `(${digitos.slice(0, 2)}) ${digitos.slice(2, 7)}-${digitos.slice(7)}`;
  if (digitos.length === 10) return `(${digitos.slice(0, 2)}) ${digitos.slice(2, 6)}-${digitos.slice(6)}`;
  return telefone;
}

function mascararTelefoneDigitado(valor: string): string {
  let digitos = valor.replace(/\D/g, "");
  if (digitos.startsWith("55") && digitos.length > 11) digitos = digitos.slice(2);
  digitos = digitos.slice(0, 11);
  if (digitos.length <= 2) return digitos ? `(${digitos}` : "";
  if (digitos.length <= 6) return `(${digitos.slice(0, 2)}) ${digitos.slice(2)}`;
  if (digitos.length <= 10) return `(${digitos.slice(0, 2)}) ${digitos.slice(2, 6)}-${digitos.slice(6)}`;
  return `(${digitos.slice(0, 2)}) ${digitos.slice(2, 7)}-${digitos.slice(7)}`;
}

function telefoneValido(valor: string): boolean {
  let digitos = valor.replace(/\D/g, "");
  if (digitos.startsWith("55") && digitos.length > 11) digitos = digitos.slice(2);
  return digitos.length === 10 || digitos.length === 11;
}

function pareceTelefone(valor: string): boolean {
  return /^[+\d\s().-]+$/.test(valor.trim()) && valor.replace(/\D/g, "").length >= 3;
}

function emailValido(valor: string): boolean {
  return !valor || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(valor);
}

export function SeletorClienteAgenda({ cliente, aoSelecionar }: Props) {
  const { chamarApi } = useAutenticacao();
  const service = useMemo(() => criarClientesAgendaService(chamarApi), [chamarApi]);
  const listaId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [busca, setBusca] = useState("");
  const [resultados, setResultados] = useState<ClienteBusca[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [buscaConcluida, setBuscaConcluida] = useState(false);
  const [erroBusca, setErroBusca] = useState(false);
  const [tentativaBusca, setTentativaBusca] = useState(0);
  const [indiceAtivo, setIndiceAtivo] = useState(-1);
  const [cadastroAberto, setCadastroAberto] = useState(false);
  const [nome, setNome] = useState("");
  const [telefone, setTelefone] = useState("");
  const [email, setEmail] = useState("");
  const [erroCadastro, setErroCadastro] = useState<string | null>(null);
  const [cadastrando, setCadastrando] = useState(false);
  const [clienteExistente, setClienteExistente] = useState<ClienteBusca | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  const termo = busca.trim();
  const listaAberta = !cliente && !cadastroAberto && termo.length >= 2 && (carregando || buscaConcluida || erroBusca);

  useEffect(() => {
    const controlador = new AbortController();
    if (cliente || cadastroAberto || termo.length < 2) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setResultados([]);
      setBuscaConcluida(false);
      setErroBusca(false);
      setCarregando(false);
      setIndiceAtivo(-1);
      return () => controlador.abort();
    }

    setCarregando(true);
    setBuscaConcluida(false);
    setErroBusca(false);
    const temporizador = window.setTimeout(() => {
      service.buscar(termo, controlador.signal).then(
        (clientes) => {
          if (controlador.signal.aborted) return;
          setResultados(clientes);
          setIndiceAtivo(clientes.length ? 0 : -1);
          setBuscaConcluida(true);
          setCarregando(false);
        },
        (erro) => {
          if (controlador.signal.aborted || (erro instanceof DOMException && erro.name === "AbortError")) return;
          setResultados([]);
          setIndiceAtivo(-1);
          setBuscaConcluida(true);
          setErroBusca(true);
          setCarregando(false);
        },
      );
    }, 300);

    return () => {
      window.clearTimeout(temporizador);
      controlador.abort();
    };
  }, [cadastroAberto, cliente, service, termo, tentativaBusca]);

  function selecionar(item: ClienteBusca) {
    aoSelecionar(item);
    setBusca(item.nome);
    setResultados([]);
    setBuscaConcluida(false);
    setIndiceAtivo(-1);
    setAviso(null);
  }

  function abrirCadastro() {
    if (pareceTelefone(termo)) {
      setNome("");
      setTelefone(mascararTelefoneDigitado(termo));
    } else {
      setNome(termo);
      setTelefone("");
    }
    setEmail("");
    setErroCadastro(null);
    setClienteExistente(null);
    setCadastroAberto(true);
  }

  function fecharCadastro() {
    setCadastroAberto(false);
    setErroCadastro(null);
    setClienteExistente(null);
    queueMicrotask(() => inputRef.current?.focus());
  }

  async function cadastrar() {
    const nomeLimpo = nome.trim();
    const emailLimpo = email.trim();
    if (!nomeLimpo) return setErroCadastro("Informe o nome do cliente.");
    if (!telefoneValido(telefone)) return setErroCadastro("Telefone inválido. Informe o DDD e o número.");
    if (!emailValido(emailLimpo)) return setErroCadastro("E-mail inválido.");

    setCadastrando(true);
    setErroCadastro(null);
    setClienteExistente(null);
    try {
      const criado = await service.cadastrar({ nome: nomeLimpo, telefone, email: emailLimpo || null });
      selecionar(criado);
      setCadastroAberto(false);
      setAviso("Cliente cadastrado e selecionado.");
    } catch (erro) {
      if (erro instanceof ErroApi && erro.status === 409 && erro.codigo === "telefone_ja_cadastrado" && erro.corpo?.id) {
        setClienteExistente({
          id: String(erro.corpo.id),
          nome: String(erro.corpo.nome ?? "Cliente existente"),
          telefone: erro.corpo.telefone ? String(erro.corpo.telefone) : null,
          email: erro.corpo.email ? String(erro.corpo.email) : null,
        });
        setErroCadastro("Este telefone já está cadastrado.");
      } else {
        setErroCadastro(erro instanceof ErroApi ? erro.message : "Não foi possível cadastrar o cliente. Tente novamente.");
      }
    } finally {
      setCadastrando(false);
    }
  }

  function aoTeclarBusca(evento: KeyboardEvent<HTMLInputElement>) {
    if (!listaAberta) return;
    if (evento.key === "ArrowDown") {
      evento.preventDefault();
      setIndiceAtivo((atual) => Math.min(atual + 1, resultados.length - 1));
    } else if (evento.key === "ArrowUp") {
      evento.preventDefault();
      setIndiceAtivo((atual) => Math.max(atual - 1, 0));
    } else if (evento.key === "Enter" && indiceAtivo >= 0 && resultados[indiceAtivo]) {
      evento.preventDefault();
      selecionar(resultados[indiceAtivo]);
    } else if (evento.key === "Escape") {
      evento.preventDefault();
      setResultados([]);
      setBuscaConcluida(false);
      setErroBusca(false);
      setIndiceAtivo(-1);
    }
  }

  return (
    <div>
      <span className={classeLabel}>Cliente</span>
      {cliente ? (
        <div className="flex min-h-11 items-center justify-between gap-3 rounded-lg border border-cyan-200 bg-cyan-50/70 px-3 py-2 dark:border-cyan-900 dark:bg-cyan-950/30">
          <div className="min-w-0">
            <p className="truncate text-sm font-medium text-gray-900 dark:text-neutral-50">{cliente.nome}</p>
            <p className="text-xs text-gray-600 dark:text-neutral-400">{formatarTelefone(cliente.telefone)}</p>
          </div>
          <button
            type="button"
            className="min-h-11 shrink-0 px-2 text-sm font-medium text-marca-primaria hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca-primaria dark:text-marca-acento"
            onClick={() => {
              aoSelecionar(null);
              setBusca("");
              setAviso(null);
              queueMicrotask(() => inputRef.current?.focus());
            }}
          >
            Trocar
          </button>
        </div>
      ) : cadastroAberto ? (
        <div className="space-y-3 rounded-xl border border-gray-200 bg-gray-50/70 p-3 dark:border-neutral-700 dark:bg-neutral-800/60">
          <div className="flex items-center justify-between gap-2">
            <p className="text-sm font-semibold text-gray-900 dark:text-neutral-50">Cadastrar novo cliente</p>
            <button type="button" className="min-h-11 px-2 text-sm text-gray-600 hover:underline dark:text-neutral-300" onClick={fecharCadastro}>
              Voltar à busca
            </button>
          </div>
          <label>
            <span className={classeLabel}>Nome</span>
            <input className={classeInput} value={nome} onChange={(evento) => setNome(evento.target.value)} maxLength={150} autoComplete="name" />
          </label>
          <label>
            <span className={classeLabel}>Telefone</span>
            <input
              className={classeInput}
              type="tel"
              inputMode="tel"
              value={telefone}
              onChange={(evento) => setTelefone(mascararTelefoneDigitado(evento.target.value))}
              placeholder="(71) 98888-7777"
              autoComplete="tel"
            />
          </label>
          <label>
            <span className={classeLabel}>E-mail (opcional)</span>
            <input className={classeInput} type="email" value={email} onChange={(evento) => setEmail(evento.target.value)} maxLength={254} autoComplete="email" />
          </label>
          {erroCadastro && <p role="alert" className="text-sm text-red-600 dark:text-red-400">{erroCadastro}</p>}
          {clienteExistente && (
            <button
              type="button"
              className={`${classeBotaoSecundario} w-full`}
              onClick={() => {
                selecionar(clienteExistente);
                setCadastroAberto(false);
              }}
            >
              Usar cliente existente: {clienteExistente.nome}
            </button>
          )}
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <button type="button" className={classeBotaoSecundario} onClick={fecharCadastro}>Cancelar</button>
            <button type="button" className={classeBotaoPrimario} disabled={cadastrando} onClick={cadastrar}>
              {cadastrando ? "Cadastrando..." : "Cadastrar e selecionar"}
            </button>
          </div>
        </div>
      ) : (
        <div>
          <input
            ref={inputRef}
            className={classeInput}
            aria-label="Buscar cliente por nome ou telefone"
            role="combobox"
            aria-autocomplete="list"
            aria-expanded={listaAberta}
            aria-controls={listaId}
            aria-activedescendant={indiceAtivo >= 0 ? `${listaId}-${indiceAtivo}` : undefined}
            placeholder="Buscar cliente por nome ou telefone"
            value={busca}
            onChange={(evento) => {
              setBusca(evento.target.value);
              setAviso(null);
            }}
            onKeyDown={aoTeclarBusca}
            autoComplete="off"
          />
          <p className="mt-1 text-xs text-gray-500 dark:text-neutral-400">Digite pelo menos 2 caracteres.</p>
          <button type="button" className="mt-2 min-h-11 text-sm font-medium text-marca-primaria hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca-primaria dark:text-marca-acento" onClick={abrirCadastro}>
            + Cadastrar novo cliente
          </button>
          {listaAberta && (
            <div id={listaId} role="listbox" aria-label="Resultados da busca de clientes" className="mt-1 max-h-56 w-full overflow-y-auto rounded-xl border border-gray-200 bg-white p-1 shadow-lg dark:border-neutral-700 dark:bg-neutral-900">
              {carregando && <p role="status" className="px-3 py-3 text-sm text-gray-600 dark:text-neutral-300">Buscando clientes...</p>}
              {!carregando && erroBusca && (
                <div className="flex items-center justify-between gap-2 px-3 py-2">
                  <p role="alert" className="text-sm text-red-600 dark:text-red-400">Não foi possível buscar clientes.</p>
                  <button
                    type="button"
                    className="min-h-11 text-sm font-medium text-marca-primaria hover:underline dark:text-marca-acento"
                    onClick={() => {
                      setErroBusca(false);
                      setBuscaConcluida(false);
                      setTentativaBusca((valor) => valor + 1);
                    }}
                  >
                    Tentar de novo
                  </button>
                </div>
              )}
              {!carregando && !erroBusca && resultados.map((item, indice) => (
                <button
                  id={`${listaId}-${indice}`}
                  role="option"
                  aria-selected={indice === indiceAtivo}
                  key={item.id}
                  type="button"
                  className={`flex min-h-12 w-full items-center justify-between gap-3 rounded-lg px-3 py-2 text-left text-sm transition ${indice === indiceAtivo ? "bg-cyan-50 text-gray-950 dark:bg-cyan-950/40 dark:text-white" : "text-gray-800 hover:bg-gray-50 dark:text-neutral-100 dark:hover:bg-neutral-800"}`}
                  onMouseEnter={() => setIndiceAtivo(indice)}
                  onClick={() => selecionar(item)}
                >
                  <span className="truncate font-medium">{item.nome}</span>
                  <span className="shrink-0 text-xs text-gray-500 dark:text-neutral-400">{formatarTelefone(item.telefone)}</span>
                </button>
              ))}
              {!carregando && !erroBusca && buscaConcluida && resultados.length === 0 && (
                <div className="px-3 py-3">
                  <p className="text-sm text-gray-600 dark:text-neutral-300">Nenhum cliente encontrado.</p>
                  <button type="button" className="mt-2 min-h-11 text-sm font-medium text-marca-primaria hover:underline dark:text-marca-acento" onClick={abrirCadastro}>Cadastrar novo cliente</button>
                </div>
              )}
            </div>
          )}
        </div>
      )}
      {aviso && <p role="status" className="mt-1 text-sm text-green-700 dark:text-green-400">{aviso}</p>}
    </div>
  );
}
