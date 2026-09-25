"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel } from "@/components/estilos";
import type {
  AgendamentoResumo,
  ClienteResumo,
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

function formatarHora(iso: string): string {
  return new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
}

const RUBRICA_STATUS: Record<string, string> = {
  Agendado: "bg-blue-50 text-blue-700 dark:bg-blue-950 dark:text-blue-300",
  Concluido: "bg-green-50 text-green-700 dark:bg-green-950 dark:text-green-300",
  Cancelado: "bg-gray-100 text-gray-500 dark:bg-neutral-800 dark:text-neutral-400 line-through",
  Faltou: "bg-red-50 text-red-700 dark:bg-red-950 dark:text-red-300",
  Reservado: "bg-yellow-50 text-yellow-700 dark:bg-yellow-950 dark:text-yellow-300",
};

export default function PaginaAgenda() {
  const { chamarApi } = useAutenticacao();

  const [profissionais, setProfissionais] = useState<ProfissionalResumo[]>([]);
  const [profissionalId, setProfissionalId] = useState<string>("");
  const [data, setData] = useState(hojeISO());
  const [agenda, setAgenda] = useState<AgendamentoResumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [modalAberto, setModalAberto] = useState(false);
  const [agendamentoParaPagar, setAgendamentoParaPagar] = useState<AgendamentoResumo | null>(null);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    chamarApi<ProfissionalResumo[]>("/painel/profissionais").then((lista) => {
      setProfissionais(lista);
      if (lista[0]) setProfissionalId((atual) => atual || lista[0].id);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const carregarAgenda = useCallback(async () => {
    if (!profissionalId) return;
    try {
      setErro(null);
      setAgenda(await chamarApi<AgendamentoResumo[]>(`/painel/agenda?profissionalId=${profissionalId}&data=${data}`));
    } catch {
      setErro("Não foi possível carregar a agenda.");
    }
  }, [chamarApi, profissionalId, data]);

  useEffect(() => {
    // Busca disparada pela troca de profissional/data selecionados nesta página.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregarAgenda();
  }, [carregarAgenda]);

  async function executarAcao(id: string, acao: "cancelar" | "concluir" | "faltou") {
    await chamarApi(`/painel/agendamentos/${id}/${acao}`, { metodo: "POST" });
    await carregarAgenda();
  }

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-wrap gap-3">
          <label>
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
        <button className={classeBotaoPrimario} disabled={!profissionalId} onClick={() => setModalAberto(true)}>
          Novo agendamento
        </button>
      </div>

      {erro && <p className="mb-4 text-sm text-red-600">{erro}</p>}

      <div className={`${classeCartao} divide-y divide-gray-100 dark:divide-neutral-800`}>
        {agenda?.length === 0 && <p className="px-4 py-6 text-sm text-gray-500 dark:text-neutral-400">Nada agendado neste dia.</p>}
        {agenda?.map((item) => (
          <div key={item.id} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
            <div>
              <div className="flex items-center gap-2">
                <span className="text-sm font-medium text-gray-900 dark:text-neutral-50">
                  {formatarHora(item.inicio)}–{formatarHora(item.fim)}
                </span>
                <span className={`rounded-full px-2 py-0.5 text-xs ${RUBRICA_STATUS[item.status] ?? ""}`}>{item.status}</span>
              </div>
              <p className="text-sm text-gray-700 dark:text-neutral-300">{item.clienteNome}</p>
              <p className="text-xs text-gray-500 dark:text-neutral-400">
                {item.servicos.join(", ")} · {formatarReais(item.total)}
              </p>
              {item.observacoes && <p className="text-xs text-gray-500 dark:text-neutral-400">Obs.: {item.observacoes}</p>}
            </div>
            {item.status === "Agendado" && (
              <div className="flex gap-2">
                <button className="text-sm text-green-700 hover:underline dark:text-green-400" onClick={() => executarAcao(item.id, "concluir")}>
                  Concluir
                </button>
                <button className="text-sm text-red-700 hover:underline dark:text-red-400" onClick={() => executarAcao(item.id, "faltou")}>
                  Faltou
                </button>
                <button className="text-sm text-gray-600 hover:underline dark:text-neutral-300" onClick={() => executarAcao(item.id, "cancelar")}>
                  Cancelar
                </button>
              </div>
            )}
            {item.status === "Concluido" && (
              <button
                className="text-sm text-green-700 hover:underline dark:text-green-400"
                onClick={() => setAgendamentoParaPagar(item)}
              >
                Registrar pagamento
              </button>
            )}
          </div>
        ))}
      </div>

      <ModalNovoAgendamento
        aberto={modalAberto}
        aoFechar={() => setModalAberto(false)}
        profissionalId={profissionalId}
        data={data}
        aoCriar={async () => {
          setModalAberto(false);
          await carregarAgenda();
        }}
      />

      <ModalRegistrarPagamento
        agendamento={agendamentoParaPagar}
        aoFechar={() => setAgendamentoParaPagar(null)}
        aoRegistrar={async () => {
          setAgendamentoParaPagar(null);
          await carregarAgenda();
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
  data,
  aoCriar,
}: {
  aberto: boolean;
  aoFechar: () => void;
  profissionalId: string;
  data: string;
  aoCriar: () => Promise<void>;
}) {
  const { chamarApi } = useAutenticacao();

  const [servicos, setServicos] = useState<ServicoResumo[]>([]);
  const [clientes, setClientes] = useState<ClienteResumo[]>([]);
  const [servicoIdsSelecionados, setServicoIdsSelecionados] = useState<string[]>([]);
  const [clienteId, setClienteId] = useState("");
  const [horariosLivres, setHorariosLivres] = useState<string[]>([]);
  const [horarioSelecionado, setHorarioSelecionado] = useState("");
  const [observacoes, setObservacoes] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    if (!aberto) return;
    // Busca disparada pela abertura do modal.
    Promise.all([
      chamarApi<ServicoResumo[]>("/painel/servicos"),
      chamarApi<ClienteResumo[]>("/painel/clientes"),
    ]).then(([listaServicos, listaClientes]) => {
      setServicos(listaServicos.filter((s) => s.ativo));
      setClientes(listaClientes);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto]);

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
      setHorariosLivres,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto, profissionalId, data, duracaoTotal]);

  function alternarServico(id: string) {
    setServicoIdsSelecionados((atual) => (atual.includes(id) ? atual.filter((s) => s !== id) : [...atual, id]));
  }

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);

    if (servicoIdsSelecionados.length === 0 || !clienteId || !horarioSelecionado) {
      setErro("Escolha ao menos um serviço, o cliente e o horário.");
      return;
    }

    setEnviando(true);
    try {
      const dados: CriarAgendamento = {
        profissionalId,
        clienteId,
        servicoIds: servicoIdsSelecionados,
        inicio: horarioSelecionado,
        observacoes: observacoes || null,
      };
      await chamarApi("/painel/agendamentos", { metodo: "POST", corpo: dados });
      setServicoIdsSelecionados([]);
      setClienteId("");
      setHorarioSelecionado("");
      setObservacoes("");
      await aoCriar();
    } catch (excecao) {
      if (excecao instanceof ErroApi && excecao.status === 409) {
        setErro("Esse horário acabou de ser preenchido por outra pessoa. Escolha outro.");
      } else {
        setErro("Não foi possível criar o agendamento (confira o expediente e os bloqueios do profissional).");
      }
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo="Novo agendamento" aberto={aberto} aoFechar={aoFechar}>
      <form onSubmit={aoEnviar} className="space-y-3">
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
                {c.nome} ({c.telefone})
              </option>
            ))}
          </select>
        </label>

        <label>
          <span className={classeLabel}>Horário ({duracaoTotal} min no total)</span>
          <select
            required
            className={classeInput}
            value={horarioSelecionado}
            onChange={(e) => setHorarioSelecionado(e.target.value)}
            disabled={duracaoTotal === 0}
          >
            <option value="">Selecione...</option>
            {horariosLivres.map((h) => (
              <option key={h} value={h}>
                {formatarHora(h)}
              </option>
            ))}
          </select>
          {duracaoTotal > 0 && horariosLivres.length === 0 && (
            <p className="mt-1 text-xs text-gray-500 dark:text-neutral-400">Nenhum horário livre nesse dia para essa duração.</p>
          )}
        </label>

        <label>
          <span className={classeLabel}>Observações (opcional)</span>
          <textarea className={classeInput} rows={2} value={observacoes} onChange={(e) => setObservacoes(e.target.value)} />
        </label>

        {erro && <p className="text-sm text-red-600">{erro}</p>}

        <div className="flex justify-end gap-2 pt-2">
          <button type="button" className={classeBotaoSecundario} onClick={aoFechar}>
            Cancelar
          </button>
          <button type="submit" disabled={enviando} className={classeBotaoPrimario}>
            {enviando ? "Criando..." : "Criar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
