"use client";

import { useEffect, useState } from "react";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeBotaoSecundario, classeInput, classeLabel } from "@/components/estilos";
import type { ClienteResumo, ConsultaForcar, ProfissionalResumo, ServicoResumo } from "@/lib/tipos";
import { formatarReais } from "@/lib/formatacao";
import { AvisoForcar } from "@/components/painel/AvisoForcar";

interface OpcoesEncaixe {
  profissionais: ProfissionalResumo[];
  servicos: ServicoResumo[];
}

/** "2026-09-28T14:30" (datetime-local) de agora, no fuso do navegador. */
function agoraLocal(): string {
  const agora = new Date();
  agora.setSeconds(0, 0);
  return new Date(agora.getTime() - agora.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
}

const formatarHora = (iso: string) => new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });

/**
 * Atendimento sem agendamento — o encaixe do balcão (seção 7). Cliente existente (busca por nome ou
 * telefone) ou cadastro rápido (nome obrigatório, telefone opcional); "Lançar e iniciar" começa agora,
 * "Apenas encaixar" usa o horário escolhido. Sem o aceite marcado, o cliente não recebe mensagens.
 */
export function ModalEncaixe({
  aberto,
  profissionalSugerido,
  aoFechar,
  aoLancar,
}: {
  aberto: boolean;
  profissionalSugerido: string;
  aoFechar: () => void;
  aoLancar: () => void;
}) {
  const { chamarApi, temPermissao } = useAutenticacao();
  const podeForcar = temPermissao("ForcarAgendamento");
  const [opcoes, setOpcoes] = useState<OpcoesEncaixe | null>(null);
  const [busca, setBusca] = useState("");
  const [resultados, setResultados] = useState<ClienteResumo[]>([]);
  const [cliente, setCliente] = useState<{ id: string; nome: string } | null>(null);
  const [novoCliente, setNovoCliente] = useState(false);
  const [nomeNovo, setNomeNovo] = useState("");
  const [telefoneNovo, setTelefoneNovo] = useState("");
  const [profissionalId, setProfissionalId] = useState("");
  const [servicoIds, setServicoIds] = useState<string[]>([]);
  const [horario, setHorario] = useState(agoraLocal);
  const [autorizou, setAutorizou] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [sugestoes, setSugestoes] = useState<string[]>([]);
  const [enviando, setEnviando] = useState(false);
  // Forçar (seção 7): o horário recusado e qual dos dois botões foi usado.
  const [forcar, setForcar] = useState<{ consulta: ConsultaForcar; iniciarAtendimento: boolean } | null>(null);

  useEffect(() => {
    if (!aberto) return;
    // Recomeça limpo a cada abertura.
    /* eslint-disable react-hooks/set-state-in-effect */
    setBusca("");
    setResultados([]);
    setCliente(null);
    setNovoCliente(false);
    setNomeNovo("");
    setTelefoneNovo("");
    setServicoIds([]);
    setHorario(agoraLocal());
    setAutorizou(false);
    setErro(null);
    setSugestoes([]);
    setForcar(null);
    /* eslint-enable react-hooks/set-state-in-effect */
    chamarApi<OpcoesEncaixe>("/painel/encaixes/opcoes")
      .then((dados) => {
        setOpcoes(dados);
        setProfissionalId(dados.profissionais.some((p) => p.id === profissionalSugerido) ? profissionalSugerido : (dados.profissionais[0]?.id ?? ""));
      })
      .catch(() => setErro("Não foi possível carregar profissionais e serviços."));
  }, [aberto, chamarApi, profissionalSugerido]);

  useEffect(() => {
    if (novoCliente || cliente || busca.trim().length < 2) return;
    // Busca com uma pequena espera, para não chamar a API a cada tecla.
    const espera = setTimeout(() => {
      chamarApi<ClienteResumo[]>(`/painel/encaixes/clientes?busca=${encodeURIComponent(busca.trim())}`)
        .then(setResultados)
        .catch(() => setResultados([]));
    }, 250);
    return () => clearTimeout(espera);
  }, [busca, chamarApi, cliente, novoCliente]);

  const servicosEscolhidos = opcoes?.servicos.filter((s) => servicoIds.includes(s.id)) ?? [];
  const duracao = servicosEscolhidos.reduce((soma, s) => soma + s.duracaoMinutos, 0);
  const total = servicosEscolhidos.reduce((soma, s) => soma + s.preco, 0);
  const clienteOk = cliente !== null || (novoCliente && nomeNovo.trim().length > 0);
  const podeEnviar = clienteOk && profissionalId && servicoIds.length > 0 && !enviando;

  function corpoDoEncaixe(iniciarAtendimento: boolean, clienteId: string | null, motivoForcar: string | null) {
    return {
      profissionalId,
      clienteId,
      novoCliente: clienteId ? null : { nome: nomeNovo.trim(), telefone: telefoneNovo.trim() || null },
      servicoIds,
      inicio: iniciarAtendimento ? null : new Date(horario).toISOString(),
      iniciarAtendimento,
      clienteAutorizouMensagens: autorizou,
      motivoForcar,
    };
  }

  async function lancar(iniciarAtendimento: boolean) {
    setErro(null);
    setSugestoes([]);
    setForcar(null);
    setEnviando(true);
    try {
      await chamarApi("/painel/encaixes", { metodo: "POST", corpo: corpoDoEncaixe(iniciarAtendimento, cliente?.id ?? null, null) });
      aoLancar();
    } catch (excecao) {
      if (excecao instanceof ErroApi) {
        setErro(excecao.message);
        // O cadastro rápido pode já ter sido feito: a próxima tentativa usa esse cliente, sem duplicar.
        const clienteId = excecao.corpo?.clienteId as string | undefined;
        if (clienteId && !cliente) setCliente({ id: clienteId, nome: nomeNovo.trim() });
        setSugestoes((excecao.corpo?.proximosHorariosLivres as string[] | undefined) ?? []);
        if (podeForcar && (excecao.status === 400 || excecao.status === 409))
          setForcar({
            consulta: { profissionalId, servicoIds, inicio: iniciarAtendimento ? null : new Date(horario).toISOString() },
            iniciarAtendimento,
          });
      } else {
        setErro("Não foi possível lançar o atendimento.");
      }
    } finally {
      setEnviando(false);
    }
  }

  async function lancarForcado(motivo: string) {
    if (!forcar) return;
    await chamarApi("/painel/encaixes", {
      metodo: "POST",
      corpo: corpoDoEncaixe(forcar.iniciarAtendimento, cliente?.id ?? null, motivo),
    });
    aoLancar();
  }

  return (
    <Modal titulo="Atendimento sem agendamento" aberto={aberto} aoFechar={aoFechar}>
      <div className="space-y-4">
        <fieldset className="space-y-2">
          <legend className={classeLabel}>Cliente</legend>
          {cliente ? (
            <div className="flex items-center justify-between rounded-lg border border-gray-200 px-3 py-2 text-sm dark:border-neutral-800">
              <span className="text-gray-900 dark:text-neutral-50">{cliente.nome}</span>
              <button type="button" className="text-marca-primaria hover:underline dark:text-marca-acento" onClick={() => setCliente(null)}>
                Trocar
              </button>
            </div>
          ) : novoCliente ? (
            <div className="grid gap-2 sm:grid-cols-2">
              <label>
                <span className={classeLabel}>Nome</span>
                <input className={classeInput} value={nomeNovo} onChange={(e) => setNomeNovo(e.target.value)} autoFocus />
              </label>
              <label>
                <span className={classeLabel}>WhatsApp (opcional)</span>
                <input type="tel" className={classeInput} placeholder="(71) 98888-7777" value={telefoneNovo} onChange={(e) => setTelefoneNovo(e.target.value)} />
              </label>
              <button type="button" className="text-left text-sm text-marca-primaria hover:underline dark:text-marca-acento" onClick={() => setNovoCliente(false)}>
                Buscar um cliente já cadastrado
              </button>
            </div>
          ) : (
            <div className="space-y-2">
              <input
                className={classeInput}
                placeholder="Buscar por nome ou telefone"
                aria-label="Buscar cliente"
                value={busca}
                onChange={(e) => setBusca(e.target.value)}
              />
              {busca.trim().length >= 2 && (
                <ul className="max-h-40 overflow-y-auto rounded-lg border border-gray-200 text-sm dark:border-neutral-800">
                  {resultados.map((c) => (
                    <li key={c.id}>
                      <button
                        type="button"
                        className="w-full px-3 py-2 text-left hover:bg-gray-50 dark:hover:bg-neutral-800"
                        onClick={() => setCliente({ id: c.id, nome: c.nome })}
                      >
                        {c.nome}
                        {c.telefone && <span className="text-gray-500 dark:text-neutral-400"> · {c.telefone}</span>}
                      </button>
                    </li>
                  ))}
                  {resultados.length === 0 && <li className="px-3 py-2 text-gray-500 dark:text-neutral-400">Nenhum cliente encontrado.</li>}
                </ul>
              )}
              <button
                type="button"
                className="text-sm text-marca-primaria hover:underline dark:text-marca-acento"
                onClick={() => {
                  setNovoCliente(true);
                  if (!/\d/.test(busca)) setNomeNovo(busca.trim());
                }}
              >
                + Cadastrar cliente novo
              </button>
            </div>
          )}
        </fieldset>

        <label className="block">
          <span className={classeLabel}>Profissional</span>
          <select className={classeInput} value={profissionalId} onChange={(e) => setProfissionalId(e.target.value)}>
            {opcoes?.profissionais.map((p) => (
              <option key={p.id} value={p.id}>
                {p.nome}
              </option>
            ))}
          </select>
        </label>

        <fieldset>
          <legend className={classeLabel}>Serviços</legend>
          <div className="max-h-40 space-y-1 overflow-y-auto">
            {opcoes?.servicos.map((s) => (
              <label key={s.id} className="flex items-center gap-2 text-sm text-gray-700 dark:text-neutral-300">
                <input
                  type="checkbox"
                  checked={servicoIds.includes(s.id)}
                  onChange={(e) => setServicoIds((atual) => (e.target.checked ? [...atual, s.id] : atual.filter((id) => id !== s.id)))}
                />
                {s.nome} · {s.duracaoMinutos} min · {formatarReais(s.preco)}
              </label>
            ))}
          </div>
          {servicoIds.length > 0 && (
            <p className="mt-1 text-xs text-gray-500 dark:text-neutral-400">
              {duracao} min · {formatarReais(total)}
            </p>
          )}
        </fieldset>

        <label className="block">
          <span className={classeLabel}>Horário (para &quot;Apenas encaixar&quot;)</span>
          <input type="datetime-local" className={classeInput} value={horario} onChange={(e) => setHorario(e.target.value)} />
        </label>

        {sugestoes.length > 0 && (
          <div className="flex flex-wrap gap-2">
            <span className="text-xs text-gray-500 dark:text-neutral-400">Horários livres:</span>
            {sugestoes.map((s) => (
              <button
                key={s}
                type="button"
                className="rounded-full border border-gray-300 px-2 py-0.5 text-xs hover:bg-gray-50 dark:border-neutral-700 dark:hover:bg-neutral-800"
                onClick={() => {
                  const d = new Date(s);
                  setHorario(new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16));
                }}
              >
                {formatarHora(s)}
              </button>
            ))}
          </div>
        )}

        <label className="flex items-start gap-2 text-sm text-gray-700 dark:text-neutral-300">
          <input type="checkbox" className="mt-0.5" checked={autorizou} onChange={(e) => setAutorizou(e.target.checked)} />
          <span>
            O cliente autorizou receber mensagens
            <span className="block text-xs text-gray-500 dark:text-neutral-400">
              Sem isso, ele não recebe confirmação nem lembrete (não passou pelo aceite do link de agendamento).
            </span>
          </span>
        </label>

        {erro && <p role="alert" className="text-sm text-red-600 dark:text-red-400">{erro}</p>}

        {forcar ? (
          <AvisoForcar consulta={forcar.consulta} aoForcar={lancarForcado} aoDesistir={() => setForcar(null)} />
        ) : (
          <div className="flex flex-wrap justify-end gap-2">
            <button type="button" className={classeBotaoSecundario} disabled={!podeEnviar} onClick={() => lancar(false)}>
              Apenas encaixar
            </button>
            <button type="button" className={classeBotaoPrimario} disabled={!podeEnviar} onClick={() => lancar(true)}>
              {enviando ? "Lançando..." : "Lançar e iniciar atendimento"}
            </button>
          </div>
        )}
      </div>
    </Modal>
  );
}
