"use client";

import { useCallback, useEffect, useState } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { ErroApi } from "@/lib/api";
import { Modal } from "@/components/Modal";
import { classeBotaoPerigo, classeBotaoSecundario, classeInput } from "@/components/estilos";
import type { AgendamentoFuturoParaExclusao, PreviaExclusao, RespostaExclusao } from "@/lib/tipos";

export type TipoCadastro = "usuario" | "profissional" | "servico";

const ROTULOS: Record<TipoCadastro, string> = { usuario: "usuário", profissional: "profissional", servico: "serviço" };

function formatarDataHora(iso: string): string {
  return new Date(iso).toLocaleString("pt-BR", { weekday: "short", day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });
}

/** Explica, em linguagem simples, o que some e o que fica no histórico (seção 7). */
function explicacao(tipo: TipoCadastro, previa: PreviaExclusao): string[] {
  const linhas: string[] = [];

  if (tipo === "usuario") {
    linhas.push("A pessoa perde o acesso ao painel na hora, em qualquer aparelho.");
    linhas.push(
      previa.temHistorico
        ? "Como ela já fez alterações registradas, o nome continua no histórico de ações. E-mail, telefone, endereço, CPF e foto são apagados, e o e-mail fica livre para um novo cadastro."
        : "Ela nunca fez nenhuma alteração registrada, então o cadastro é apagado de vez.",
    );
  } else if (tipo === "profissional") {
    linhas.push("Sai da lista de profissionais, da página de agendamento e do limite do seu plano.");
    linhas.push(
      previa.temHistorico
        ? "Os atendimentos que já aconteceram continuam mostrando o nome no histórico e no financeiro. Telefone, e-mail, endereço, CPF e foto são apagados."
        : "Nunca teve nenhum agendamento, então o cadastro é apagado de vez, com horários e bloqueios.",
    );
  } else {
    linhas.push("Sai da lista de serviços e da página de agendamento na hora.");
    if (previa.agendamentosFuturos > 0)
      linhas.push(
        `${previa.agendamentosFuturos === 1 ? "O agendamento já marcado" : `Os ${previa.agendamentosFuturos} agendamentos já marcados`} com este serviço continua${previa.agendamentosFuturos === 1 ? "" : "m"} valendo.`,
      );
    linhas.push(
      previa.temHistorico
        ? "Os agendamentos antigos e o financeiro continuam com o nome e o preço da época."
        : "Nunca foi agendado, então o cadastro é apagado de vez.",
    );
  }

  linhas.push("Não dá para desfazer. Se quiser só tirar de uso por um tempo, use Desativar.");
  return linhas;
}

export function ModalExclusao({
  tipo,
  id,
  aoFechar,
  aoExcluir,
}: {
  tipo: TipoCadastro;
  /** Nulo = fechado. */
  id: string | null;
  aoFechar: () => void;
  aoExcluir: () => Promise<void> | void;
}) {
  const { chamarApi } = useAutenticacao();
  const caminho = `/painel/${tipo === "usuario" ? "usuarios" : tipo === "profissional" ? "profissionais" : "servicos"}/${id}`;
  const [previa, setPrevia] = useState<PreviaExclusao | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [confirmado, setConfirmado] = useState(false);
  const [excluindo, setExcluindo] = useState(false);

  const carregar = useCallback(async () => {
    if (!id) return;
    try {
      setPrevia(await chamarApi<PreviaExclusao>(`${caminho}/exclusao`));
    } catch (excecao) {
      setErro(excecao instanceof ErroApi && excecao.status === 403 ? "Você não tem permissão para excluir cadastros." : "Não foi possível carregar.");
    }
  }, [chamarApi, caminho, id]);

  useEffect(() => {
    // Nova exclusão aberta: começa do zero e busca a prévia.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setPrevia(null);
    setErro(null);
    setConfirmado(false);
    carregar();
  }, [carregar]);

  async function excluir() {
    setExcluindo(true);
    setErro(null);
    try {
      await chamarApi<RespostaExclusao>(caminho, { metodo: "DELETE" });
      await aoExcluir();
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível excluir.");
      await carregar();
    } finally {
      setExcluindo(false);
    }
  }

  return (
    <Modal titulo={`Excluir ${ROTULOS[tipo]}${previa ? `: ${previa.nome}` : ""}`} aberto={id !== null} aoFechar={aoFechar}>
      <div data-testid="modal-exclusao" className="space-y-3 text-sm text-gray-700 dark:text-neutral-300">
        {!previa && !erro && <p className="text-gray-500">Carregando...</p>}

        {previa && (
          <>
            <ul className="list-disc space-y-1 pl-5">
              {explicacao(tipo, previa).map((linha) => (
                <li key={linha}>{linha}</li>
              ))}
            </ul>

            {previa.bloqueio && (
              <p role="alert" className="rounded-lg bg-amber-50 p-3 text-amber-900 dark:bg-amber-950/50 dark:text-amber-100">
                {previa.bloqueio}
              </p>
            )}

            {previa.futuros.length > 0 && (
              <ul className="max-h-72 space-y-2 overflow-y-auto">
                {previa.futuros.map((futuro) => (
                  <ItemAgendamentoFuturo
                    key={futuro.id}
                    futuro={futuro}
                    caminhoProfissional={caminho}
                    aoResolver={carregar}
                    aoErro={setErro}
                  />
                ))}
              </ul>
            )}

            {!previa.bloqueio && (
              <label className="flex items-start gap-2">
                <input type="checkbox" className="mt-0.5" checked={confirmado} onChange={(e) => setConfirmado(e.target.checked)} />
                <span>Entendi o que vai acontecer e quero excluir.</span>
              </label>
            )}
          </>
        )}

        {erro && (
          <p role="alert" className="text-red-600 dark:text-red-400">
            {erro}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <button type="button" className={classeBotaoSecundario} onClick={aoFechar}>
            Voltar
          </button>
          <button
            type="button"
            className={classeBotaoPerigo}
            disabled={!previa || previa.bloqueio !== null || !confirmado || excluindo}
            onClick={excluir}
          >
            {excluindo ? "Excluindo..." : "Excluir"}
          </button>
        </div>
      </div>
    </Modal>
  );
}

/** Um agendamento futuro do profissional a excluir: transferir para outro ou cancelar avisando o cliente. */
function ItemAgendamentoFuturo({
  futuro,
  caminhoProfissional,
  aoResolver,
  aoErro,
}: {
  futuro: AgendamentoFuturoParaExclusao;
  caminhoProfissional: string;
  aoResolver: () => Promise<void>;
  aoErro: (mensagem: string | null) => void;
}) {
  const { chamarApi } = useAutenticacao();
  const [destino, setDestino] = useState(futuro.profissionaisPossiveis[0]?.id ?? "");
  const [enviando, setEnviando] = useState(false);
  const base = `${caminhoProfissional}/agendamentos-futuros/${futuro.id}`;

  async function executar(acao: () => Promise<unknown>) {
    setEnviando(true);
    aoErro(null);
    try {
      await acao();
      await aoResolver();
    } catch (excecao) {
      aoErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível concluir.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <li data-testid={`futuro-${futuro.id}`} className="rounded-lg border border-gray-200 p-3 dark:border-neutral-800">
      <p className="font-medium text-gray-900 dark:text-neutral-50">
        {formatarDataHora(futuro.inicio)} · {futuro.cliente}
      </p>
      <p className="text-xs text-gray-500 dark:text-neutral-400">{futuro.servicos.join(", ")}</p>
      <div className="mt-2 flex flex-wrap items-center gap-2">
        {futuro.profissionaisPossiveis.length > 0 ? (
          <>
            <select
              aria-label="Transferir para"
              className={`${classeInput} w-auto flex-1`}
              value={destino}
              onChange={(e) => setDestino(e.target.value)}
            >
              {futuro.profissionaisPossiveis.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.nome}
                </option>
              ))}
            </select>
            <button
              type="button"
              disabled={enviando || !destino}
              className={classeBotaoSecundario}
              onClick={() => executar(() => chamarApi(`${base}/transferir`, { metodo: "POST", corpo: { novoProfissionalId: destino } }))}
            >
              Transferir
            </button>
          </>
        ) : (
          <span className="flex-1 text-xs text-gray-500 dark:text-neutral-400">Nenhum outro profissional faz todos esses serviços.</span>
        )}
        <button
          type="button"
          disabled={enviando}
          className={classeBotaoPerigo}
          onClick={() => {
            if (confirm(`Cancelar o agendamento de ${futuro.cliente}? O cliente recebe um aviso do cancelamento.`))
              executar(() => chamarApi(`${base}/cancelar`, { metodo: "POST" }));
          }}
        >
          Cancelar
        </button>
      </div>
    </li>
  );
}
