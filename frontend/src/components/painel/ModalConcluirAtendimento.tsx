"use client";

import { useEffect, useState } from "react";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeBotaoSecundario } from "@/components/estilos";
import { CamposVenda, chaveVendedor, corpoVendedor, itensDaVenda, type LinhaVenda } from "@/components/painel/CamposVenda";
import type { AgendamentoResumo, OpcoesVenda } from "@/lib/tipos";
import { formatarReais } from "@/lib/formatacao";

/**
 * "Concluir atendimento" (seção 7). Quem vende produtos ganha a seção "Produtos vendidos", com o profissional do
 * atendimento sugerido como vendedor. Primeiro conclui, depois lança a venda: se a venda falhar (ex.: estoque), o
 * atendimento já está concluído e a janela fica aberta só para corrigir e lançar os produtos.
 */
export function ModalConcluirAtendimento({
  agendamento,
  aoFechar,
}: {
  agendamento: AgendamentoResumo | null;
  /** `mudou`: o atendimento foi concluído (e talvez a venda lançada) — recarregar a agenda. */
  aoFechar: (mudou: boolean) => void;
}) {
  const { chamarApi, temPermissao } = useAutenticacao();
  const podeVender = temPermissao("VenderProdutos");
  const [opcoes, setOpcoes] = useState<OpcoesVenda | null>(null);
  const [linhas, setLinhas] = useState<LinhaVenda[]>([]);
  const [vendedor, setVendedor] = useState("");
  const [concluido, setConcluido] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    if (!agendamento) return;
    /* eslint-disable react-hooks/set-state-in-effect */
    setOpcoes(null);
    setLinhas([]);
    setVendedor("");
    setConcluido(false);
    setErro(null);
    /* eslint-enable react-hooks/set-state-in-effect */
    if (!podeVender) return;
    chamarApi<OpcoesVenda>(`/painel/vendas/opcoes?agendamentoId=${agendamento.id}`)
      .then((dados) => {
        setOpcoes(dados);
        setVendedor(chaveVendedor(dados.vendedorSugerido));
      })
      .catch(() => setErro("Não foi possível carregar os produtos."));
  }, [agendamento, podeVender, chamarApi]);

  const itens = itensDaVenda(linhas);

  async function confirmar() {
    if (!agendamento) return;
    setErro(null);
    setEnviando(true);
    try {
      if (!concluido) {
        await chamarApi(`/painel/agendamentos/${agendamento.id}/concluir`, { metodo: "POST" });
        setConcluido(true);
      }
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível concluir o atendimento.");
      setEnviando(false);
      return;
    }

    if (itens.length === 0) {
      setEnviando(false);
      aoFechar(true);
      return;
    }

    try {
      await chamarApi("/painel/vendas", {
        metodo: "POST",
        corpo: { itens, ...corpoVendedor(vendedor), agendamentoId: agendamento.id },
      });
      aoFechar(true);
    } catch (excecao) {
      const motivo = excecao instanceof ErroApi ? excecao.message : "Não foi possível lançar a venda.";
      setErro(`O atendimento foi concluído, mas os produtos não foram lançados: ${motivo} Corrija e tente de novo.`);
    } finally {
      setEnviando(false);
    }
  }

  const vendaIncompleta = itens.length > 0 && (!vendedor || itens.length !== linhas.length);

  return (
    <Modal titulo="Concluir atendimento" aberto={agendamento !== null} aoFechar={() => aoFechar(concluido)}>
      {agendamento && (
        <div className="space-y-4">
          <p className="text-sm text-gray-700 dark:text-neutral-300">
            {agendamento.clienteNome} · {agendamento.servicos.join(", ")} · {formatarReais(agendamento.total)}
            {concluido && <span className="ml-2 font-medium text-green-700 dark:text-green-400">Concluído</span>}
          </p>

          {podeVender && (
            <section aria-label="Produtos vendidos" className="space-y-2">
              <h3 className="text-sm font-semibold text-gray-900 dark:text-neutral-50">Produtos vendidos</h3>
              {opcoes ? (
                <CamposVenda opcoes={opcoes} linhas={linhas} aoMudarLinhas={setLinhas} vendedor={vendedor} aoMudarVendedor={setVendedor} />
              ) : (
                !erro && <p className="text-sm text-gray-500 dark:text-neutral-400">Carregando produtos...</p>
              )}
            </section>
          )}

          {erro && <p className="text-sm text-red-600">{erro}</p>}

          <div className="flex justify-end gap-2 pt-2">
            <button type="button" className={classeBotaoSecundario} onClick={() => aoFechar(concluido)}>
              {concluido ? "Fechar sem os produtos" : "Cancelar"}
            </button>
            <button type="button" disabled={enviando || vendaIncompleta} className={classeBotaoPrimario} onClick={confirmar}>
              {enviando ? "Salvando..." : concluido ? "Lançar produtos" : "Concluir atendimento"}
            </button>
          </div>
        </div>
      )}
    </Modal>
  );
}
