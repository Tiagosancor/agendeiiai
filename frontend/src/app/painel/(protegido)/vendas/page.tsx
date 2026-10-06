"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { CabecalhoOperacional } from "@/components/painel/OperacionaisVisuais";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel } from "@/components/estilos";
import { CamposVenda, chaveVendedor, corpoVendedor, itensDaVenda, type LinhaVenda } from "@/components/painel/CamposVenda";
import type { ClienteResumo, OpcoesVenda, VendaResumo } from "@/lib/tipos";
import { dataLocalIso, formatarReais } from "@/lib/formatacao";

const formatarHora = (iso: string) => new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });

/** Vendas de produto (seção 7): a venda avulsa do balcão e as vendas do dia (inclusive as lançadas ao concluir atendimento). */
export default function PaginaVendas() {
  const { chamarApi, temPermissao } = useAutenticacao();
  const podeVender = temPermissao("VenderProdutos");
  const podeEstornar = temPermissao("GerenciarEstoque");
  const [estornando, setEstornando] = useState<VendaResumo | null>(null);
  const [data, setData] = useState(dataLocalIso());
  const [vendas, setVendas] = useState<VendaResumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [novaAberta, setNovaAberta] = useState(false);

  const carregar = useCallback(async () => {
    try {
      setErro(null);
      setVendas(await chamarApi<VendaResumo[]>(`/painel/vendas?de=${data}&ate=${data}`));
    } catch {
      setErro("Não foi possível carregar as vendas.");
    }
  }, [chamarApi, data]);

  useEffect(() => {
    if (!podeVender) return;
    // Busca disparada pela troca de data nesta página.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar, podeVender]);

  if (!podeVender) {
    return <p className="text-sm text-gray-600 dark:text-neutral-400">Você não tem permissão para vender produtos.</p>;
  }

  const totalDia = vendas?.filter((v) => !v.estornada).reduce((soma, v) => soma + v.total, 0) ?? 0;

  return (
    <div className="painel-operacional">
      <CabecalhoOperacional titulo="Vendas" descricao="Consulte as vendas do dia e registre vendas de produtos.">
        <button className={classeBotaoPrimario} onClick={() => setNovaAberta(true)}>
          Nova venda
        </button>
      </CabecalhoOperacional>
      <div className="painel-operacional-toolbar">
        <label>
          <span className={classeLabel}>Data</span>
          <input type="date" className={classeInput} value={data} onChange={(e) => setData(e.target.value)} />
        </label>
      </div>

      {erro && <p className="mb-4 text-sm text-red-600">{erro}</p>}
      {!vendas && !erro && <p role="status" className="painel-operacional-carregando">Carregando...</p>}

      <div className={`${classeCartao} painel-operacional-lista divide-y divide-gray-100 dark:divide-neutral-800`}>
        {vendas?.length === 0 && <p className="px-4 py-6 text-sm text-gray-500 dark:text-neutral-400">Nenhuma venda neste dia.</p>}
        {vendas?.map((venda) => (
          <div key={venda.id} className="flex flex-wrap items-start justify-between gap-2 px-4 py-3" role="article" data-estornada={venda.estornada} aria-label={`Venda das ${formatarHora(venda.data)}`}>
            <div>
              <p className={`text-sm font-medium text-gray-900 dark:text-neutral-50 ${venda.estornada ? "line-through opacity-60" : ""}`}>
                {formatarHora(venda.data)} · {venda.itens.map((i) => (i.quantidade === 1 ? i.produto : `${i.produto} × ${i.quantidade}`)).join(", ")}
              </p>
              {venda.estornada && (
                <p className="text-xs font-medium text-red-700 dark:text-red-400">Estornada: {venda.motivoEstorno}</p>
              )}
              <p className="text-xs text-gray-500 dark:text-neutral-400">
                Vendido por {venda.vendedor}
                {venda.cliente ? ` · ${venda.cliente}` : ""}
                {venda.agendamentoId ? " · no atendimento" : ""}
              </p>
            </div>
            <span className="flex flex-col items-end gap-1">
              <span className={`text-sm font-medium text-gray-900 dark:text-neutral-50 ${venda.estornada ? "line-through opacity-60" : ""}`}>
                {formatarReais(venda.total)}
              </span>
              {podeEstornar && !venda.estornada && (
                <button type="button" className="text-xs text-red-700 hover:underline dark:text-red-400" onClick={() => setEstornando(venda)}>
                  Estornar
                </button>
              )}
            </span>
          </div>
        ))}
        {vendas && vendas.length > 0 && (
          <p className="px-4 py-3 text-right text-sm font-semibold text-gray-900 dark:text-neutral-50">Total do dia: {formatarReais(totalDia)}</p>
        )}
      </div>

      {estornando && (
        <ModalEstorno
          venda={estornando}
          aoFechar={async (estornou) => {
            setEstornando(null);
            if (estornou) await carregar();
          }}
        />
      )}

      <ModalNovaVenda
        aberta={novaAberta}
        aoFechar={async (lancou) => {
          setNovaAberta(false);
          if (lancou) {
            setData(dataLocalIso());
            await carregar();
          }
        }}
      />
    </div>
  );
}

/**
 * Estorno (seção 7): desfaz a venda — os produtos voltam ao estoque e ela sai do faturamento e da comissão de quem
 * vendeu. Só quem gerencia o estoque; motivo obrigatório. A venda continua na lista, marcada como estornada.
 */
function ModalEstorno({ venda, aoFechar }: { venda: VendaResumo; aoFechar: (estornou: boolean) => void }) {
  const { chamarApi } = useAutenticacao();
  const [motivo, setMotivo] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function estornar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await chamarApi(`/painel/vendas/${venda.id}/estornar`, { metodo: "POST", corpo: { motivo } });
      aoFechar(true);
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível estornar.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo="Estornar venda" aberto aoFechar={() => aoFechar(false)}>
      <form onSubmit={estornar} className="space-y-3">
        <p className="text-sm text-gray-700 dark:text-neutral-300">
          {venda.itens.map((i) => `${i.produto} × ${i.quantidade}`).join(", ")} · {formatarReais(venda.total)} · vendido por {venda.vendedor}
        </p>
        <p className="text-sm text-gray-600 dark:text-neutral-400">
          Os produtos voltam ao estoque e a venda sai do faturamento e da comissão de quem vendeu. Ela continua na lista, marcada como
          estornada.
        </p>
        <label className="block">
          <span className={classeLabel}>Motivo (obrigatório)</span>
          <input required maxLength={500} className={classeInput} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        </label>
        {erro && <p className="text-sm text-red-600">{erro}</p>}
        <div className="flex justify-end gap-2 pt-2">
          <button type="button" className={classeBotaoSecundario} onClick={() => aoFechar(false)}>
            Cancelar
          </button>
          <button type="submit" disabled={enviando || !motivo.trim()} className={classeBotaoPrimario}>
            {enviando ? "Estornando..." : "Estornar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function ModalNovaVenda({ aberta, aoFechar }: { aberta: boolean; aoFechar: (lancou: boolean) => void }) {
  const { chamarApi } = useAutenticacao();
  const [opcoes, setOpcoes] = useState<OpcoesVenda | null>(null);
  const [linhas, setLinhas] = useState<LinhaVenda[]>([]);
  const [vendedor, setVendedor] = useState("");
  const [busca, setBusca] = useState("");
  const [resultados, setResultados] = useState<ClienteResumo[]>([]);
  const [cliente, setCliente] = useState<{ id: string; nome: string } | null>(null);
  const [novoCliente, setNovoCliente] = useState(false);
  const [nomeNovo, setNomeNovo] = useState("");
  const [telefoneNovo, setTelefoneNovo] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    if (!aberta) return;
    // Recomeça limpo a cada abertura, com uma linha de produto já aberta.
    /* eslint-disable react-hooks/set-state-in-effect */
    setOpcoes(null);
    setLinhas([{ chave: crypto.randomUUID(), produtoId: "", quantidade: "1", valorUnitario: "" }]);
    setVendedor("");
    setBusca("");
    setResultados([]);
    setCliente(null);
    setNovoCliente(false);
    setNomeNovo("");
    setTelefoneNovo("");
    setErro(null);
    /* eslint-enable react-hooks/set-state-in-effect */
    chamarApi<OpcoesVenda>("/painel/vendas/opcoes")
      .then((dados) => {
        setOpcoes(dados);
        setVendedor(chaveVendedor(dados.vendedorSugerido));
      })
      .catch(() => setErro("Não foi possível carregar os produtos."));
  }, [aberta, chamarApi]);

  useEffect(() => {
    if (novoCliente || cliente || busca.trim().length < 2) return;
    const espera = setTimeout(() => {
      chamarApi<ClienteResumo[]>(`/painel/vendas/clientes?busca=${encodeURIComponent(busca.trim())}`)
        .then(setResultados)
        .catch(() => setResultados([]));
    }, 250);
    return () => clearTimeout(espera);
  }, [busca, chamarApi, cliente, novoCliente]);

  const itens = itensDaVenda(linhas);
  const podeEnviar = itens.length > 0 && itens.length === linhas.length && vendedor !== "" && !enviando;

  async function lancar() {
    setErro(null);
    setEnviando(true);
    try {
      await chamarApi("/painel/vendas", {
        metodo: "POST",
        corpo: {
          itens,
          ...corpoVendedor(vendedor),
          clienteId: cliente?.id ?? null,
          novoCliente: !cliente && novoCliente && nomeNovo.trim() ? { nome: nomeNovo.trim(), telefone: telefoneNovo.trim() || null } : null,
        },
      });
      aoFechar(true);
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível lançar a venda.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo="Nova venda" aberto={aberta} aoFechar={() => aoFechar(false)}>
      <div className="space-y-4">
        <fieldset className="space-y-2">
          <legend className={classeLabel}>Cliente (opcional)</legend>
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

        {opcoes ? (
          <CamposVenda opcoes={opcoes} linhas={linhas} aoMudarLinhas={setLinhas} vendedor={vendedor} aoMudarVendedor={setVendedor} />
        ) : (
          !erro && <p className="text-sm text-gray-500 dark:text-neutral-400">Carregando produtos...</p>
        )}

        {erro && <p className="text-sm text-red-600">{erro}</p>}

        <div className="flex justify-end gap-2 pt-2">
          <button type="button" className={classeBotaoSecundario} onClick={() => aoFechar(false)}>
            Cancelar
          </button>
          <button type="button" disabled={!podeEnviar} className={classeBotaoPrimario} onClick={lancar}>
            {enviando ? "Lançando..." : "Lançar venda"}
          </button>
        </div>
      </div>
    </Modal>
  );
}
