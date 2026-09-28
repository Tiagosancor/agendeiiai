"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeInput, classeLabel } from "@/components/estilos";
import type { LinhaValores, ModoAjusteValor, TipoAjusteValor, ValoresAtendimento } from "@/lib/tipos";
import { formatarReais } from "@/lib/formatacao";

/** Mesma conta do servidor (`AjusteValorAtendimento.Calcular`): sempre sobre o preço original. */
function calcularFinal(preco: number, tipo: TipoAjusteValor, modo: ModoAjusteValor, valor: number): number {
  const diferenca = modo === "Reais" ? valor : Math.round(preco * valor) / 100;
  return Math.round((tipo === "Desconto" ? preco - diferenca : preco + diferenca) * 100) / 100;
}

const descreverAjuste = (tipo: TipoAjusteValor, modo: ModoAjusteValor, valor: number) =>
  `${tipo === "Desconto" ? "desconto" : "acréscimo"} de ${
    modo === "Reais" ? formatarReais(valor) : `${valor.toLocaleString("pt-BR", { maximumFractionDigits: 2 })}%`
  }`;

/**
 * Valores do atendimento e ajuste por serviço (seção 7). O formulário só aparece quando a API diz
 * que o usuário pode ajustar agora (`podeAjustar`); senão, fica só o histórico — o profissional vê
 * que o valor foi ajustado e o motivo, porque isso mexe na comissão dele.
 */
export function ModalAjusteValor({ agendamentoId, aoFechar }: { agendamentoId: string | null; aoFechar: (mudou: boolean) => void }) {
  const { chamarApi } = useAutenticacao();
  const [valores, setValores] = useState<ValoresAtendimento | null>(null);
  const [mudou, setMudou] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  const carregar = useCallback(async () => {
    if (!agendamentoId) return;
    try {
      setValores(await chamarApi<ValoresAtendimento>(`/painel/agendamentos/${agendamentoId}/valores`));
    } catch {
      setErro("Não foi possível carregar os valores do atendimento.");
    }
  }, [chamarApi, agendamentoId]);

  useEffect(() => {
    // Busca disparada ao abrir o modal.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setValores(null);
    setErro(null);
    setMudou(false);
    carregar();
  }, [carregar]);

  const concluido = valores?.status === "Concluido";

  return (
    <Modal titulo={concluido ? "Corrigir valor do atendimento" : "Valores do atendimento"} aberto={agendamentoId !== null} aoFechar={() => aoFechar(mudou)}>
      {erro && <p className="text-sm text-red-600 dark:text-red-400">{erro}</p>}
      {!valores && !erro && <p className="text-sm text-gray-500 dark:text-neutral-400">Carregando...</p>}
      {valores && (
        <div className="space-y-5">
          {concluido && valores.podeAjustar && (
            <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800 dark:bg-amber-950 dark:text-amber-300">
              Este atendimento já foi concluído. A correção recalcula a comissão e fica registrada na auditoria.
            </p>
          )}
          {valores.linhas.map((linha) => (
            <LinhaAjuste
              key={linha.linhaId}
              agendamentoId={valores.agendamentoId}
              linha={linha}
              podeAjustar={valores.podeAjustar}
              aoAjustar={async () => {
                setMudou(true);
                await carregar();
              }}
            />
          ))}
          <div className="border-t border-gray-100 pt-3 text-sm dark:border-neutral-800">
            {valores.descontoCupom > 0 && (
              <p className="text-gray-600 dark:text-neutral-300">Cupom: − {formatarReais(valores.descontoCupom)}</p>
            )}
            <p className="font-semibold text-gray-900 dark:text-neutral-50">Total a cobrar: {formatarReais(valores.total)}</p>
          </div>
        </div>
      )}
    </Modal>
  );
}

function LinhaAjuste({
  agendamentoId,
  linha,
  podeAjustar,
  aoAjustar,
}: {
  agendamentoId: string;
  linha: LinhaValores;
  podeAjustar: boolean;
  aoAjustar: () => Promise<void>;
}) {
  const { chamarApi } = useAutenticacao();
  const [tipo, setTipo] = useState<TipoAjusteValor>("Desconto");
  const [modo, setModo] = useState<ModoAjusteValor>("Reais");
  const [valor, setValor] = useState("");
  const [motivo, setMotivo] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  const numero = Number(valor.trim().replace(",", "."));
  const valido = valor.trim() !== "" && !Number.isNaN(numero) && numero >= 0;
  const final = valido ? calcularFinal(linha.precoOriginal, tipo, modo, numero) : null;

  async function confirmar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    if (!valido || final === null) return setErro("Informe um valor.");
    if (final < 0) return setErro("O valor final não pode ficar negativo.");
    if (!motivo.trim()) return setErro("O motivo é obrigatório.");

    setEnviando(true);
    try {
      await chamarApi(`/painel/agendamentos/${agendamentoId}/servicos/${linha.linhaId}/ajuste`, {
        metodo: "POST",
        corpo: { tipo, modo, valor: numero, motivo },
      });
      setValor("");
      setMotivo("");
      await aoAjustar();
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível ajustar o valor.");
    } finally {
      setEnviando(false);
    }
  }

  const ajustado = linha.valorCobrado !== linha.precoOriginal;

  return (
    <section className="space-y-2">
      <div className="flex items-baseline justify-between gap-3">
        <h3 className="font-medium text-gray-900 dark:text-neutral-50">{linha.servico}</h3>
        <span className="text-sm">
          {ajustado && <span className="mr-2 text-gray-400 line-through">{formatarReais(linha.precoOriginal)}</span>}
          <span className="font-semibold text-gray-900 dark:text-neutral-50">{formatarReais(linha.valorCobrado)}</span>
        </span>
      </div>

      {linha.ajustes.length > 0 && (
        <ul className="space-y-1 text-xs text-gray-600 dark:text-neutral-400">
          {linha.ajustes.map((a, i) => (
            <li key={i}>
              {new Date(a.em).toLocaleString("pt-BR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" })} ·{" "}
              {descreverAjuste(a.tipo, a.modo, a.valorInformado)} ({formatarReais(a.valorAntes)} → {formatarReais(a.valorDepois)})
              {a.aposConclusao && " · correção após concluir"}
              {a.por && ` · ${a.por}`} — “{a.motivo}”
            </li>
          ))}
        </ul>
      )}

      {podeAjustar && (
        <form onSubmit={confirmar} className="space-y-2 rounded-lg border border-gray-200 p-3 dark:border-neutral-800">
          <div className="flex flex-wrap gap-4 text-sm text-gray-700 dark:text-neutral-300">
            <fieldset className="flex gap-3">
              <legend className="sr-only">Tipo de ajuste</legend>
              {(["Desconto", "Acrescimo"] as const).map((t) => (
                <label key={t} className="flex items-center gap-1">
                  <input type="radio" name={`tipo-${linha.linhaId}`} checked={tipo === t} onChange={() => setTipo(t)} />
                  {t === "Desconto" ? "Desconto" : "Acréscimo"}
                </label>
              ))}
            </fieldset>
            <fieldset className="flex gap-3">
              <legend className="sr-only">Em reais ou percentual</legend>
              {(["Reais", "Percentual"] as const).map((m) => (
                <label key={m} className="flex items-center gap-1">
                  <input type="radio" name={`modo-${linha.linhaId}`} checked={modo === m} onChange={() => setModo(m)} />
                  {m === "Reais" ? "R$" : "%"}
                </label>
              ))}
            </fieldset>
          </div>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-[8rem_1fr]">
            <label>
              <span className={classeLabel}>{modo === "Reais" ? "Valor (R$)" : "Percentual (%)"}</span>
              <input inputMode="decimal" className={classeInput} value={valor} onChange={(e) => setValor(e.target.value)} />
            </label>
            <label>
              <span className={classeLabel}>Motivo (obrigatório)</span>
              <input className={classeInput} maxLength={300} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
            </label>
          </div>
          <div className="flex items-center justify-between gap-3">
            <span className={`text-sm ${final !== null && final < 0 ? "text-red-600 dark:text-red-400" : "text-gray-700 dark:text-neutral-300"}`}>
              {final !== null ? <>Valor final: <strong>{formatarReais(final)}</strong></> : "Informe o valor para ver o final"}
            </span>
            <button type="submit" className={classeBotaoPrimario} disabled={enviando || !valido || (final ?? 0) < 0 || !motivo.trim()}>
              {enviando ? "Salvando..." : "Confirmar ajuste"}
            </button>
          </div>
          {erro && <p role="alert" className="text-sm text-red-600 dark:text-red-400">{erro}</p>}
        </form>
      )}
    </section>
  );
}
