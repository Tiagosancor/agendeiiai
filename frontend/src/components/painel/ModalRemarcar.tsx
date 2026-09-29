"use client";

import { useEffect, useState, type FormEvent } from "react";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { AvisoForcar } from "@/components/painel/AvisoForcar";
import { classeBotaoPrimario, classeBotaoSecundario, classeInput, classeLabel } from "@/components/estilos";
import type { AgendamentoResumo, ConsultaForcar } from "@/lib/tipos";
import { dataLocalIso } from "@/lib/formatacao";

const formatarHora = (iso: string) => new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });

/**
 * Remarcar um agendamento no painel (seção 7): escolhe o dia e um horário livre com a mesma duração. Quem tem "forçar
 * agendamento" também pode digitar outro horário e, se o sistema recusar, ver as regras quebradas e forçar com motivo
 * (o mover forçado da API). Remarcar pelo caminho normal tira a marca de forçado.
 */
export function ModalRemarcar({ agendamento, aoFechar }: { agendamento: AgendamentoResumo | null; aoFechar: (mudou: boolean) => void }) {
  const { chamarApi, temPermissao } = useAutenticacao();
  const podeForcar = temPermissao("ForcarAgendamento");
  const [data, setData] = useState("");
  const [livres, setLivres] = useState<string[]>([]);
  const [horario, setHorario] = useState("");
  const [outroHorario, setOutroHorario] = useState("");
  const [consultaForcar, setConsultaForcar] = useState<ConsultaForcar | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  const duracao = agendamento ? Math.round((new Date(agendamento.fim).getTime() - new Date(agendamento.inicio).getTime()) / 60000) : 0;

  useEffect(() => {
    if (!agendamento) return;
    /* eslint-disable react-hooks/set-state-in-effect */
    setData(dataLocalIso(new Date(agendamento.inicio)));
    setHorario("");
    setOutroHorario("");
    setConsultaForcar(null);
    setErro(null);
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [agendamento]);

  useEffect(() => {
    if (!agendamento || !data || duracao <= 0) return;
    // Busca disparada pela troca de dia.
    chamarApi<string[]>(`/painel/profissionais/${agendamento.profissionalId}/horarios-livres?data=${data}&duracaoMinutos=${duracao}`)
      .then(setLivres)
      .catch(() => setLivres([]));
  }, [agendamento, data, duracao, chamarApi]);

  const novoInicio = outroHorario ? new Date(`${data}T${outroHorario}`).toISOString() : horario;

  async function remarcar(evento: FormEvent) {
    evento.preventDefault();
    if (!agendamento || !novoInicio) return;
    setErro(null);
    setConsultaForcar(null);
    setEnviando(true);
    try {
      await chamarApi(`/painel/agendamentos/${agendamento.id}/mover`, { metodo: "PUT", corpo: { novoInicio } });
      aoFechar(true);
    } catch (excecao) {
      if (excecao instanceof ErroApi && (excecao.status === 400 || excecao.status === 409) && podeForcar) {
        setErro(excecao.message);
        setConsultaForcar({ profissionalId: agendamento.profissionalId, servicoIds: null, inicio: novoInicio, agendamentoId: agendamento.id });
      } else {
        setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível remarcar.");
      }
    } finally {
      setEnviando(false);
    }
  }

  async function forcar(motivo: string) {
    if (!agendamento) return;
    await chamarApi(`/painel/agendamentos/forcados/${agendamento.id}/mover`, { metodo: "PUT", corpo: { novoInicio, motivo } });
    aoFechar(true);
  }

  return (
    <Modal titulo="Remarcar" aberto={agendamento !== null} aoFechar={() => aoFechar(false)}>
      {agendamento && (
        <form onSubmit={remarcar} className="space-y-3">
          <p className="text-sm text-gray-600 dark:text-neutral-400">
            {agendamento.clienteNome} · {agendamento.servicos.join(", ")} · marcado para {new Date(agendamento.inicio).toLocaleDateString("pt-BR")} às {formatarHora(agendamento.inicio)} ({duracao} min)
          </p>
          <label className="block">
            <span className={classeLabel}>Novo dia</span>
            <input
              type="date"
              required
              className={classeInput}
              value={data}
              onChange={(e) => {
                setData(e.target.value);
                setHorario("");
                setConsultaForcar(null);
              }}
            />
          </label>
          <label className="block">
            <span className={classeLabel}>Novo horário</span>
            <select
              required={!outroHorario}
              className={classeInput}
              value={horario}
              onChange={(e) => {
                setHorario(e.target.value);
                setOutroHorario("");
                setConsultaForcar(null);
              }}
            >
              <option value="">Selecione...</option>
              {livres.map((h) => (
                <option key={h} value={h}>
                  {formatarHora(h)}
                </option>
              ))}
            </select>
            {livres.length === 0 && <p className="mt-1 text-xs text-gray-500 dark:text-neutral-400">Nenhum horário livre nesse dia para essa duração.</p>}
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
                  setHorario("");
                  setConsultaForcar(null);
                }}
              />
            </label>
          )}
          {erro && <p className="text-sm text-red-600">{erro}</p>}
          {consultaForcar ? (
            <AvisoForcar consulta={consultaForcar} aoForcar={forcar} aoDesistir={() => setConsultaForcar(null)} />
          ) : (
            <div className="flex justify-end gap-2 pt-2">
              <button type="button" className={classeBotaoSecundario} onClick={() => aoFechar(false)}>
                Cancelar
              </button>
              <button type="submit" disabled={enviando || !novoInicio} className={classeBotaoPrimario}>
                {enviando ? "Remarcando..." : "Remarcar"}
              </button>
            </div>
          )}
        </form>
      )}
    </Modal>
  );
}
