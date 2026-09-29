"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { ModalAjusteValor } from "@/components/painel/ModalAjusteValor";
import { ModalEncaixe } from "@/components/painel/ModalEncaixe";
import { ModalConcluirAtendimento } from "@/components/painel/ModalConcluirAtendimento";
import { ModalRemarcar } from "@/components/painel/ModalRemarcar";
import { MinhaAgenda } from "@/components/painel/MinhaAgenda";
import { SemanaAgenda, inicioDaSemana, somarDias } from "@/components/painel/SemanaAgenda";
import { AvisoForcar } from "@/components/painel/AvisoForcar";
import { GradeDoDia } from "@/components/painel/GradeDoDia";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel } from "@/components/estilos";
import type {
  AgendaSemana,
  AgendamentoResumo,
  ClienteResumo,
  ConsultaForcar,
  CriarAgendamento,
  FormaPagamento,
  ProfissionalResumo,
  RegistrarPagamento,
  ServicoResumo,
} from "@/lib/tipos";
import { FORMAS_PAGAMENTO } from "@/lib/tipos";
import { dataLocalIso, formatarReais } from "@/lib/formatacao";

function hojeISO(): string {
  return dataLocalIso();
}

/** Compara horários pelo instante, não pelo texto (o mesmo horário pode vir com offsets diferentes). */
function mesmoInstante(a: string, b: string): boolean {
  return new Date(a).getTime() === new Date(b).getTime();
}

function formatarHora(iso: string): string {
  return new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
}

const ROTULO_STATUS: Record<string, string> = { EmAtendimento: "Em atendimento", Concluido: "Concluído" };

const RUBRICA_STATUS: Record<string, string> = {
  Agendado: "bg-blue-50 text-blue-700 dark:bg-blue-950 dark:text-blue-300",
  EmAtendimento: "bg-violet-50 text-violet-700 dark:bg-violet-950 dark:text-violet-300",
  Concluido: "bg-green-50 text-green-700 dark:bg-green-950 dark:text-green-300",
  Cancelado: "bg-gray-100 text-gray-500 dark:bg-neutral-800 dark:text-neutral-400 line-through",
  Faltou: "bg-red-50 text-red-700 dark:bg-red-950 dark:text-red-300",
  Reservado: "bg-yellow-50 text-yellow-700 dark:bg-yellow-950 dark:text-yellow-300",
};

/** Quem gerencia a agenda vê a de todos; o Profissional logado sem essa permissão vê só a própria (seção 7). */
export default function PaginaAgenda() {
  const { temPermissao } = useAutenticacao();
  return temPermissao("GerenciarAgenda") ? <AgendaGeral /> : <MinhaAgenda />;
}

function AgendaGeral() {
  const { chamarApi, temPermissao } = useAutenticacao();

  const [profissionais, setProfissionais] = useState<ProfissionalResumo[]>([]);
  const [profissionalId, setProfissionalId] = useState<string>("");
  const [data, setData] = useState(hojeISO());
  const [agenda, setAgenda] = useState<AgendamentoResumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [modalAberto, setModalAberto] = useState(false);
  const [agendamentoParaPagar, setAgendamentoParaPagar] = useState<AgendamentoResumo | null>(null);
  const [agendamentoValores, setAgendamentoValores] = useState<string | null>(null);
  const [agendamentoParaConcluir, setAgendamentoParaConcluir] = useState<AgendamentoResumo | null>(null);
  const [agendamentoParaRemarcar, setAgendamentoParaRemarcar] = useState<AgendamentoResumo | null>(null);
  const [semana, setSemana] = useState<AgendaSemana | null>(null);
  const podeAjustarValor = temPermissao("AjustarValorAtendimento");
  const [encaixeAberto, setEncaixeAberto] = useState(false);
  // Visão "Dia" em grade é a padrão (seção 7); a lista de um profissional guarda as ações de cada atendimento.
  const [visao, setVisao] = useState<"grade" | "lista" | "semana">("grade");
  const [versaoGrade, setVersaoGrade] = useState(0);
  const [horarioInicialNovo, setHorarioInicialNovo] = useState<string | null>(null);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    chamarApi<ProfissionalResumo[]>("/painel/agenda/profissionais").then((lista) => {
      setProfissionais(lista);
      if (lista[0]) setProfissionalId((atual) => atual || lista[0].id);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Trocar profissional e data em seguida dispara duas buscas; só a última pode escrever na tela
  // (a resposta mais antiga, chegando depois, mostrava a agenda do filtro anterior).
  const ultimaBusca = useRef(0);

  const carregarAgenda = useCallback(async () => {
    if (!profissionalId) return;
    const busca = ++ultimaBusca.current;
    try {
      setErro(null);
      const resultado = await chamarApi<AgendamentoResumo[]>(`/painel/agenda?profissionalId=${profissionalId}&data=${data}`);
      if (busca === ultimaBusca.current) setAgenda(resultado);
    } catch {
      if (busca === ultimaBusca.current) setErro("Não foi possível carregar a agenda.");
    }
  }, [chamarApi, profissionalId, data]);

  useEffect(() => {
    // Busca disparada pela troca de profissional/data selecionados nesta página.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregarAgenda();
  }, [carregarAgenda]);

  // Visão "Semana" (seção 7): o profissional escolhido, a semana (segunda a domingo) do dia selecionado.
  // Como na lista do dia, só a última busca escreve na tela (trocar profissional e data em seguida dispara duas).
  const inicioSemana = inicioDaSemana(data);
  const ultimaBuscaSemana = useRef(0);
  const carregarSemana = useCallback(async () => {
    if (visao !== "semana" || !profissionalId) return;
    const busca = ++ultimaBuscaSemana.current;
    setSemana(null);
    try {
      const resultado = await chamarApi<AgendaSemana>(`/painel/agenda/semana?profissionalId=${profissionalId}&inicio=${inicioSemana}`);
      if (busca === ultimaBuscaSemana.current) setSemana(resultado);
    } catch {
      if (busca === ultimaBuscaSemana.current) setErro("Não foi possível carregar a semana.");
    }
  }, [chamarApi, visao, profissionalId, inicioSemana]);

  useEffect(() => {
    // Busca disparada pela troca de visão, profissional ou semana.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregarSemana();
  }, [carregarSemana]);

  /** Depois de qualquer mudança: a grade do dia, a lista do profissional e a semana. */
  async function recarregarTudo() {
    setVersaoGrade((v) => v + 1);
    await Promise.all([carregarAgenda(), carregarSemana()]);
  }

  async function executarAcao(id: string, acao: "iniciar" | "cancelar" | "faltou" | "reabrir") {
    try {
      setErro(null);
      await chamarApi(`/painel/agendamentos/${id}/${acao}`, { metodo: "POST" });
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível concluir a ação.");
    }
    await recarregarTudo();
  }

  function reabrir(item: AgendamentoResumo) {
    const aviso =
      "Reabrir este atendimento?\n\nEle volta para Agendado e, para manter o caixa certo, " +
      "a comissão, o pagamento registrado e o selo de fidelidade dele são desfeitos. Ao concluir de novo, tudo é registrado outra vez.";
    if (confirm(aviso)) executarAcao(item.id, "reabrir");
  }

  return (
    <div>
      <div className="mb-3 flex gap-1 border-b border-gray-200 dark:border-neutral-800" role="tablist" aria-label="Visão da agenda">
        {(
          [
            ["grade", "Dia (todos)"],
            ["lista", "Por profissional"],
            ["semana", "Semana"],
          ] as const
        ).map(([valor, rotulo]) => (
          <button
            key={valor}
            type="button"
            role="tab"
            aria-selected={visao === valor}
            className={`-mb-px border-b-2 px-3 py-2 text-sm font-medium ${
              visao === valor
                ? "border-marca-primaria text-marca-primaria dark:border-marca-acento dark:text-marca-acento"
                : "border-transparent text-gray-500 hover:text-gray-700 dark:text-neutral-400 dark:hover:text-neutral-200"
            }`}
            onClick={() => setVisao(valor)}
          >
            {rotulo}
          </button>
        ))}
      </div>

      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-wrap gap-3">
          <label className={visao === "grade" ? "hidden" : undefined}>
            <span className={classeLabel}>Profissional</span>
            <select className={classeInput} value={profissionalId} onChange={(e) => setProfissionalId(e.target.value)}>
              {profissionais.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.nome}
                </option>
              ))}
            </select>
          </label>
          <label>
            <span className={classeLabel}>Data</span>
            <input type="date" className={classeInput} value={data} onChange={(e) => setData(e.target.value)} />
          </label>
        </div>
        <div className="flex flex-wrap gap-2">
          {temPermissao("LancarAtendimentoSemAgendamento") && (
            <button className={classeBotaoSecundario} onClick={() => setEncaixeAberto(true)}>
              Atendimento sem agendamento
            </button>
          )}
          <button
            className={classeBotaoPrimario}
            disabled={!profissionalId}
            onClick={() => {
              setHorarioInicialNovo(null);
              setModalAberto(true);
            }}
          >
            Novo agendamento
          </button>
        </div>
      </div>

      {erro && <p className="mb-4 text-sm text-red-600">{erro}</p>}

      {visao === "grade" && (
        <GradeDoDia
          data={data}
          versao={versaoGrade}
          aoEscolherLivre={(idProfissional, inicio) => {
            setProfissionalId(idProfissional);
            setHorarioInicialNovo(inicio);
            setModalAberto(true);
          }}
          aoAbrirAgendamento={(idProfissional) => {
            setProfissionalId(idProfissional);
            setVisao("lista");
          }}
        />
      )}

      {visao === "semana" && (
        <SemanaAgenda
          semana={semana}
          aoMudarSemana={(dias) => setData((atual) => somarDias(atual, dias))}
          aoEscolherDia={(dia) => {
            setData(dia);
            setVisao("lista");
          }}
        />
      )}

      <div className={`${classeCartao} divide-y divide-gray-100 dark:divide-neutral-800 ${visao !== "lista" ? "hidden" : ""}`}>
        {agenda?.length === 0 && <p className="px-4 py-6 text-sm text-gray-500 dark:text-neutral-400">Nada agendado neste dia.</p>}
        {agenda?.map((item) => (
          <div key={item.id} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
            <div>
              <div className="flex items-center gap-2">
                <span className="text-sm font-medium text-gray-900 dark:text-neutral-50">
                  {formatarHora(item.inicio)}–{formatarHora(item.fim)}
                </span>
                <span className={`rounded-full px-2 py-0.5 text-xs ${RUBRICA_STATUS[item.status] ?? ""}`}>
                  {ROTULO_STATUS[item.status] ?? item.status}
                </span>
                {item.forcado && (
                  <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800 dark:bg-amber-900/50 dark:text-amber-200">
                    Forçado
                  </span>
                )}
              </div>
              <p className="text-sm text-gray-700 dark:text-neutral-300">{item.clienteNome}</p>
              <p className="text-xs text-gray-500 dark:text-neutral-400">
                {item.servicos.join(", ")} · {formatarReais(item.total)}
              </p>
              {item.observacoes && <p className="text-xs text-gray-500 dark:text-neutral-400">Obs.: {item.observacoes}</p>}
              {item.forcado && (
                <details className="text-xs text-amber-800 dark:text-amber-200">
                  <summary className="cursor-pointer">
                    Forçado{item.forcadoPor ? ` por ${item.forcadoPor}` : ""}: {item.forcadoMotivo}
                  </summary>
                  <ul className="mt-1 list-disc pl-5">
                    {item.forcadoRegras?.map((regra) => (
                      <li key={regra}>{regra}</li>
                    ))}
                  </ul>
                </details>
              )}
            </div>
            {item.status === "Agendado" && (
              <div className="flex flex-wrap gap-2">
                <button className="text-sm font-medium text-violet-700 hover:underline dark:text-violet-300" onClick={() => executarAcao(item.id, "iniciar")}>
                  Iniciar atendimento
                </button>
                <button className="text-sm text-green-700 hover:underline dark:text-green-400" onClick={() => setAgendamentoParaConcluir(item)}>
                  Concluir
                </button>
                <button className="text-sm text-red-700 hover:underline dark:text-red-400" onClick={() => executarAcao(item.id, "faltou")}>
                  Faltou
                </button>
                <button className="text-sm text-marca-primaria hover:underline dark:text-marca-acento" onClick={() => setAgendamentoParaRemarcar(item)}>
                  Remarcar
                </button>
                <button className="text-sm text-gray-600 hover:underline dark:text-neutral-300" onClick={() => executarAcao(item.id, "cancelar")}>
                  Cancelar
                </button>
              </div>
            )}
            {item.status === "EmAtendimento" && (
              <div className="flex flex-wrap gap-2">
                {podeAjustarValor && (
                  <button className="text-sm font-medium text-violet-700 hover:underline dark:text-violet-300" onClick={() => setAgendamentoValores(item.id)}>
                    Ajustar valor
                  </button>
                )}
                <button className="text-sm text-green-700 hover:underline dark:text-green-400" onClick={() => setAgendamentoParaConcluir(item)}>
                  Concluir
                </button>
                <button className="text-sm text-gray-600 hover:underline dark:text-neutral-300" onClick={() => executarAcao(item.id, "cancelar")}>
                  Cancelar
                </button>
              </div>
            )}
            {item.status === "Concluido" && (
              <div className="flex flex-wrap gap-2">
                <button
                  className="text-sm text-green-700 hover:underline dark:text-green-400"
                  onClick={() => setAgendamentoParaPagar(item)}
                >
                  Registrar pagamento
                </button>
                {temPermissao("VerFinanceiro") && (
                  <button className="text-sm text-gray-600 hover:underline dark:text-neutral-300" onClick={() => reabrir(item)}>
                    Reabrir
                  </button>
                )}
                {podeAjustarValor && (
                  <button className="text-sm text-gray-600 hover:underline dark:text-neutral-300" onClick={() => setAgendamentoValores(item.id)}>
                    Valores
                  </button>
                )}
              </div>
            )}
          </div>
        ))}
      </div>

      <ModalNovoAgendamento
        aberto={modalAberto}
        aoFechar={() => setModalAberto(false)}
        profissionalId={profissionalId}
        nomeProfissional={profissionais.find((p) => p.id === profissionalId)?.nome ?? ""}
        data={data}
        horarioInicial={horarioInicialNovo}
        aoCriar={async () => {
          setModalAberto(false);
          await recarregarTudo();
        }}
      />

      <ModalEncaixe
        aberto={encaixeAberto}
        profissionalSugerido={profissionalId}
        aoFechar={() => setEncaixeAberto(false)}
        aoLancar={async () => {
          setEncaixeAberto(false);
          await recarregarTudo();
        }}
      />

      <ModalAjusteValor
        agendamentoId={agendamentoValores}
        aoFechar={async (mudou) => {
          setAgendamentoValores(null);
          if (mudou) await recarregarTudo();
        }}
      />

      <ModalRemarcar
        agendamento={agendamentoParaRemarcar}
        aoFechar={async (mudou) => {
          setAgendamentoParaRemarcar(null);
          if (mudou) await recarregarTudo();
        }}
      />

      <ModalConcluirAtendimento
        agendamento={agendamentoParaConcluir}
        aoFechar={async (mudou) => {
          setAgendamentoParaConcluir(null);
          if (mudou) await recarregarTudo();
        }}
      />

      <ModalRegistrarPagamento
        agendamento={agendamentoParaPagar}
        aoFechar={() => setAgendamentoParaPagar(null)}
        aoRegistrar={async () => {
          setAgendamentoParaPagar(null);
          await recarregarTudo();
        }}
      />
    </div>
  );
}

function ModalRegistrarPagamento({
  agendamento,
  aoFechar,
  aoRegistrar,
}: {
  agendamento: AgendamentoResumo | null;
  aoFechar: () => void;
  aoRegistrar: () => Promise<void>;
}) {
  const { chamarApi } = useAutenticacao();
  const [valor, setValor] = useState("");
  const [forma, setForma] = useState<FormaPagamento>("Pix");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    // Sincroniza o valor sugerido com o total do agendamento sempre que o modal abre.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (agendamento) setValor(agendamento.total.toFixed(2));
  }, [agendamento]);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    if (!agendamento) return;

    setErro(null);
    setEnviando(true);
    try {
      const dados: RegistrarPagamento = { agendamentoId: agendamento.id, valor: Number(valor), forma };
      await chamarApi("/painel/pagamentos", { metodo: "POST", corpo: dados });
      await aoRegistrar();
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível registrar o pagamento.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo="Registrar pagamento" aberto={agendamento !== null} aoFechar={aoFechar}>
      <form onSubmit={aoEnviar} className="space-y-3">
        <label>
          <span className={classeLabel}>Valor (R$)</span>
          <input required type="number" min={0.01} step="0.01" className={classeInput} value={valor} onChange={(e) => setValor(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Forma de pagamento</span>
          <select className={classeInput} value={forma} onChange={(e) => setForma(e.target.value as FormaPagamento)}>
            {FORMAS_PAGAMENTO.map((f) => (
              <option key={f.valor} value={f.valor}>
                {f.rotulo}
              </option>
            ))}
          </select>
        </label>

        {erro && <p className="text-sm text-red-600">{erro}</p>}

        <div className="flex justify-end gap-2 pt-2">
          <button type="button" className={classeBotaoSecundario} onClick={aoFechar}>
            Cancelar
          </button>
          <button type="submit" disabled={enviando} className={classeBotaoPrimario}>
            {enviando ? "Registrando..." : "Registrar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function ModalNovoAgendamento({
  aberto,
  aoFechar,
  profissionalId,
  nomeProfissional,
  data,
  horarioInicial,
  aoCriar,
}: {
  aberto: boolean;
  aoFechar: () => void;
  profissionalId: string;
  nomeProfissional: string;
  data: string;
  /** Horário clicado na grade do dia (já vem escolhido). */
  horarioInicial: string | null;
  aoCriar: () => Promise<void>;
}) {
  const { chamarApi, temPermissao } = useAutenticacao();
  const podeForcar = temPermissao("ForcarAgendamento");

  const [servicos, setServicos] = useState<ServicoResumo[]>([]);
  const [clientes, setClientes] = useState<ClienteResumo[]>([]);
  const [servicoIdsSelecionados, setServicoIdsSelecionados] = useState<string[]>([]);
  const [clienteId, setClienteId] = useState("");
  const [horariosLivres, setHorariosLivres] = useState<string[]>([]);
  const [horarioSelecionado, setHorarioSelecionado] = useState("");
  const [observacoes, setObservacoes] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  // Forçar (seção 7): horário digitado fora da lista de livres e o aviso das regras quebradas.
  const [outroHorario, setOutroHorario] = useState("");
  const [consultaForcar, setConsultaForcar] = useState<ConsultaForcar | null>(null);

  useEffect(() => {
    if (!aberto) return;
    // Horário clicado na grade: já vem escolhido (a lista de livres depende dos serviços, que ainda não foram marcados).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setHorarioSelecionado(horarioInicial ?? "");
    setOutroHorario("");
    setConsultaForcar(null);
    // Busca disparada pela abertura do modal.
    Promise.all([
      chamarApi<ServicoResumo[]>("/painel/agenda/servicos"),
      chamarApi<ClienteResumo[]>("/painel/clientes"),
    ]).then(([listaServicos, listaClientes]) => {
      setServicos(listaServicos.filter((s) => s.ativo));
      setClientes(listaClientes);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto, horarioInicial]);

  // O horário escolhido na grade fica na lista mesmo que a API não o devolva como livre para a duração.
  const opcoesHorario =
    horarioSelecionado && !horariosLivres.some((h) => mesmoInstante(h, horarioSelecionado)) ? [horarioSelecionado, ...horariosLivres] : horariosLivres;

  const duracaoTotal = useMemo(
    () => servicos.filter((s) => servicoIdsSelecionados.includes(s.id)).reduce((soma, s) => soma + s.duracaoMinutos, 0),
    [servicos, servicoIdsSelecionados],
  );

  useEffect(() => {
    if (!aberto || !profissionalId || duracaoTotal === 0) {
      // Sincroniza o select de horários com a duração atual (0 = nada selecionável ainda).
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setHorariosLivres([]);
      return;
    }
    // Busca disparada pela troca de serviços selecionados (muda a duração total).
    chamarApi<string[]>(`/painel/profissionais/${profissionalId}/horarios-livres?data=${data}&duracaoMinutos=${duracaoTotal}`).then(
      (livres) => {
        setHorariosLivres(livres);
        // O horário clicado na grade passa a usar o mesmo texto da lista, para o select reconhecê-lo.
        setHorarioSelecionado((atual) => (atual && livres.find((h) => mesmoInstante(h, atual))) || atual);
      },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto, profissionalId, data, duracaoTotal]);

  function alternarServico(id: string) {
    setServicoIdsSelecionados((atual) => (atual.includes(id) ? atual.filter((s) => s !== id) : [...atual, id]));
  }

  // Horário digitado vale no dia da agenda, no fuso do navegador (como o resto da tela).
  const inicio = outroHorario ? new Date(`${data}T${outroHorario}`).toISOString() : horarioSelecionado;

  function dadosDoAgendamento(): CriarAgendamento {
    return { profissionalId, clienteId, servicoIds: servicoIdsSelecionados, inicio, observacoes: observacoes || null };
  }

  async function concluirCriacao() {
    setServicoIdsSelecionados([]);
    setClienteId("");
    setHorarioSelecionado("");
    setOutroHorario("");
    setObservacoes("");
    setConsultaForcar(null);
    await aoCriar();
  }

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setConsultaForcar(null);

    if (servicoIdsSelecionados.length === 0 || !clienteId || !inicio) {
      setErro("Escolha ao menos um serviço, o cliente e o horário.");
      return;
    }

    setEnviando(true);
    try {
      await chamarApi("/painel/agendamentos", { metodo: "POST", corpo: dadosDoAgendamento() });
      await concluirCriacao();
    } catch (excecao) {
      if (excecao instanceof ErroApi && (excecao.status === 400 || excecao.status === 409) && podeForcar) {
        setErro(excecao.message);
        setConsultaForcar({ profissionalId, servicoIds: servicoIdsSelecionados, inicio });
      } else if (excecao instanceof ErroApi && excecao.status === 409) {
        setErro("Esse horário acabou de ser preenchido por outra pessoa. Escolha outro.");
      } else {
        setErro("Não foi possível criar o agendamento (confira o expediente e os bloqueios do profissional).");
      }
    } finally {
      setEnviando(false);
    }
  }

  async function forcar(motivo: string) {
    await chamarApi("/painel/agendamentos/forcados", { metodo: "POST", corpo: { ...dadosDoAgendamento(), motivo } });
    await concluirCriacao();
  }

  return (
    <Modal titulo="Novo agendamento" aberto={aberto} aoFechar={aoFechar}>
      <form onSubmit={aoEnviar} className="space-y-3">
        {nomeProfissional && (
          <p className="text-sm text-gray-600 dark:text-neutral-400">
            Com <strong className="text-gray-900 dark:text-neutral-50">{nomeProfissional}</strong>
          </p>
        )}
        <div>
          <span className={classeLabel}>Serviços</span>
          <div className="max-h-32 space-y-1 overflow-y-auto rounded-lg border border-gray-200 p-2 dark:border-neutral-700">
            {servicos.map((s) => (
              <label key={s.id} className="flex items-center gap-2 text-sm text-gray-700 dark:text-neutral-200">
                <input type="checkbox" checked={servicoIdsSelecionados.includes(s.id)} onChange={() => alternarServico(s.id)} />
                {s.nome} — {formatarReais(s.preco)} ({s.duracaoMinutos} min)
              </label>
            ))}
            {servicos.length === 0 && <p className="text-sm text-gray-500 dark:text-neutral-400">Nenhum serviço ativo.</p>}
          </div>
        </div>

        <label>
          <span className={classeLabel}>Cliente</span>
          <select required className={classeInput} value={clienteId} onChange={(e) => setClienteId(e.target.value)}>
            <option value="">Selecione...</option>
            {clientes.map((c) => (
              <option key={c.id} value={c.id}>
                {c.nome}
                {c.telefone && ` (${c.telefone})`}
              </option>
            ))}
          </select>
        </label>

        <label>
          <span className={classeLabel}>Horário ({duracaoTotal} min no total)</span>
          <select
            required={!outroHorario}
            className={classeInput}
            value={horarioSelecionado}
            onChange={(e) => {
              setHorarioSelecionado(e.target.value);
              setOutroHorario("");
            }}
            disabled={duracaoTotal === 0}
          >
            <option value="">Selecione...</option>
            {opcoesHorario.map((h) => (
              <option key={h} value={h}>
                {formatarHora(h)}
              </option>
            ))}
          </select>
          {duracaoTotal > 0 && horariosLivres.length === 0 && (
            <p className="mt-1 text-xs text-gray-500 dark:text-neutral-400">Nenhum horário livre nesse dia para essa duração.</p>
          )}
        </label>

        {podeForcar && (
          <label className="block">
            <span className={classeLabel}>Outro horário (fora da lista — pode exigir forçar)</span>
            <input
              type="time"
              className={classeInput}
              value={outroHorario}
              onChange={(e) => {
                setOutroHorario(e.target.value);
                setHorarioSelecionado("");
                setConsultaForcar(null);
              }}
            />
          </label>
        )}

        <label>
          <span className={classeLabel}>Observações (opcional)</span>
          <textarea className={classeInput} rows={2} value={observacoes} onChange={(e) => setObservacoes(e.target.value)} />
        </label>

        {erro && <p className="text-sm text-red-600">{erro}</p>}

        {consultaForcar ? (
          <AvisoForcar consulta={consultaForcar} aoForcar={forcar} aoDesistir={() => setConsultaForcar(null)} />
        ) : (
          <div className="flex justify-end gap-2 pt-2">
            <button type="button" className={classeBotaoSecundario} onClick={aoFechar}>
              Cancelar
            </button>
            <button type="submit" disabled={enviando} className={classeBotaoPrimario}>
              {enviando ? "Criando..." : "Criar"}
            </button>
          </div>
        )}
      </form>
    </Modal>
  );
}
