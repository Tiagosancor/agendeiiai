"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { CabecalhoOperacional } from "@/components/painel/OperacionaisVisuais";
import { ErroApi } from "@/lib/api";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import { formatarReais } from "@/lib/formatacao";
import type { MovimentoEstoqueResumo, ProdutoResumo, SaldoDoProfissional } from "@/lib/tipos";
import { corpoPessoa, SeletorPessoaSaldo } from "@/components/painel/SaldosDevedores";

const ROTULO_SITUACAO: Record<ProdutoResumo["situacao"], { texto: string; classe: string }> = {
  Normal: { texto: "Em estoque", classe: "bg-green-50 text-green-700 dark:bg-green-950 dark:text-green-300" },
  EstoqueBaixo: { texto: "Estoque baixo", classe: "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300" },
  Esgotado: { texto: "Esgotado", classe: "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300" },
  Inativo: { texto: "Inativo", classe: "bg-gray-100 text-gray-500 dark:bg-neutral-800 dark:text-neutral-400" },
};

const ROTULO_MOVIMENTO: Record<MovimentoEstoqueResumo["tipo"], string> = {
  Entrada: "Entrada",
  Venda: "Venda",
  ConsumoInterno: "Consumo interno",
  Ajuste: "Ajuste",
};

type Acao = { tipo: "editar" | "entrada" | "ajuste" | "consumo" | "historico"; produto: ProdutoResumo } | null;

/**
 * Estoque (seção 7): a quantidade nunca é digitada direto — muda por entrada (reposição), ajuste (contagem,
 * com motivo), venda e consumo interno (produto usado pelo profissional, que vira saldo devedor dele). "Esgotado" e "Estoque baixo" ficam em destaque no topo.
 */
export default function PaginaEstoque() {
  const { chamarApi, temPermissao } = useAutenticacao();
  const [produtos, setProdutos] = useState<ProdutoResumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [novoAberto, setNovoAberto] = useState(false);
  const [acao, setAcao] = useState<Acao>(null);

  const carregar = useCallback(async () => {
    try {
      setProdutos(await chamarApi<ProdutoResumo[]>("/painel/estoque/produtos"));
    } catch {
      setErro("Não foi possível carregar o estoque.");
    }
  }, [chamarApi]);

  useEffect(() => {
    // Busca disparada pela montagem.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  if (!temPermissao("GerenciarEstoque")) return <p className="text-sm text-gray-500 dark:text-neutral-400">Sem permissão para gerenciar o estoque.</p>;

  async function alternarAtivo(produto: ProdutoResumo) {
    await chamarApi(`/painel/estoque/produtos/${produto.id}/${produto.ativo ? "desativar" : "ativar"}`, { metodo: "POST" });
    await carregar();
  }

  async function excluir(produto: ProdutoResumo) {
    if (!confirm(`Excluir ${produto.nome}? O histórico de entradas e ajustes dele também some.`)) return;
    try {
      await chamarApi(`/painel/estoque/produtos/${produto.id}`, { metodo: "DELETE" });
      await carregar();
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível excluir o produto.");
    }
  }

  const esgotados = produtos?.filter((p) => p.situacao === "Esgotado") ?? [];
  const baixos = produtos?.filter((p) => p.situacao === "EstoqueBaixo") ?? [];
  const fechar = async (mudou: boolean) => {
    setAcao(null);
    if (mudou) await carregar();
  };

  return (
    <div className="painel-operacional">
      <CabecalhoOperacional titulo="Estoque" descricao="Acompanhe produtos, quantidades e movimentações do estoque.">
        <button className={classeBotaoPrimario} onClick={() => setNovoAberto(true)}>
          Novo produto
        </button>
      </CabecalhoOperacional>

      {erro && <p className="text-sm text-red-600">{erro}</p>}
      {!produtos && !erro && <p role="status" className="painel-operacional-carregando">Carregando...</p>}

      {esgotados.length > 0 && (
        <section aria-label="Esgotado" className="rounded-lg border border-red-300 bg-red-50 px-4 py-3 text-sm dark:border-red-800 dark:bg-red-950/40">
          <h2 className="font-semibold text-red-800 dark:text-red-300">Esgotado</h2>
          <p className="text-red-800 dark:text-red-200">{esgotados.map((p) => p.nome).join(", ")}</p>
        </section>
      )}
      {baixos.length > 0 && (
        <section aria-label="Estoque baixo" className="rounded-lg border border-amber-300 bg-amber-50 px-4 py-3 text-sm dark:border-amber-800 dark:bg-amber-950/40">
          <h2 className="font-semibold text-amber-900 dark:text-amber-300">Estoque baixo</h2>
          <p className="text-amber-900 dark:text-amber-100">
            {baixos.map((p) => `${p.nome} (${p.quantidadeEstoque}, mínimo ${p.quantidadeMinima})`).join(", ")}
          </p>
        </section>
      )}

      <div className={classeCartao}>
        <div className="overflow-x-auto">
          <table role="table" aria-label="Produtos em estoque" className="painel-operacional-cards w-full min-w-175">
            <thead role="rowgroup" className="border-b border-gray-200 dark:border-neutral-800">
              <tr role="row">
                <th role="columnheader" scope="col" data-label="Produto" className={classeTh}>Produto</th>
                <th role="columnheader" scope="col" data-label="Em estoque" className={classeTh}>Em estoque</th>
                <th role="columnheader" scope="col" data-label="Custo" className={classeTh}>Custo</th>
                <th role="columnheader" scope="col" data-label="Venda" className={classeTh}>Venda</th>
                <th role="columnheader" scope="col" data-label="Situação" className={classeTh}>Situação</th>
                <th role="columnheader" scope="col" data-label="Ações" className={classeTh}>Ações</th>
              </tr>
            </thead>
            <tbody role="rowgroup" className="divide-y divide-gray-100 dark:divide-neutral-800">
              {produtos?.map((produto) => (
                <tr role="row" key={produto.id}>
                  <td role="cell" data-label="Produto" className={classeTd}>
                    {produto.nome}
                    {produto.categoria && <span className="block text-xs text-gray-500 dark:text-neutral-400">{produto.categoria}</span>}
                  </td>
                  <td role="cell" data-label="Em estoque" className={classeTd}>
                    {produto.quantidadeEstoque}
                    <span className="block text-xs text-gray-500 dark:text-neutral-400">mínimo {produto.quantidadeMinima}</span>
                  </td>
                  <td role="cell" data-label="Custo" className={classeTd}>{formatarReais(produto.precoCusto)}</td>
                  <td role="cell" data-label="Venda" className={classeTd}>{formatarReais(produto.precoVenda)}</td>
                  <td role="cell" data-label="Situação" className={classeTd}>
                    <span className={`painel-operacional-situacao ${ROTULO_SITUACAO[produto.situacao].classe}`}>
                      {ROTULO_SITUACAO[produto.situacao].texto}
                    </span>
                  </td>
                  <td role="cell" data-label="Ações" className={`${classeTd} space-x-3 whitespace-nowrap`}>
                    <BotaoLink onClick={() => setAcao({ tipo: "entrada", produto })}>Entrada</BotaoLink>
                    <BotaoLink onClick={() => setAcao({ tipo: "ajuste", produto })}>Ajuste</BotaoLink>
                    {produto.ativo && <BotaoLink onClick={() => setAcao({ tipo: "consumo", produto })}>Consumo</BotaoLink>}
                    <BotaoLink onClick={() => setAcao({ tipo: "historico", produto })}>Histórico</BotaoLink>
                    <BotaoLink onClick={() => setAcao({ tipo: "editar", produto })}>Editar</BotaoLink>
                    <BotaoLink cinza onClick={() => alternarAtivo(produto)}>
                      {produto.ativo ? "Desativar" : "Ativar"}
                    </BotaoLink>
                    <BotaoLink vermelho onClick={() => excluir(produto)}>
                      Excluir
                    </BotaoLink>
                  </td>
                </tr>
              ))}
              {produtos?.length === 0 && (
                <tr role="row">
                  <td role="cell" className={classeTd} colSpan={6}>
                    Nenhum produto cadastrado ainda.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      <ModalProduto
        aberto={novoAberto}
        produto={null}
        aoFechar={async (mudou) => {
          setNovoAberto(false);
          if (mudou) await carregar();
        }}
      />
      {acao?.tipo === "editar" && <ModalProduto aberto produto={acao.produto} aoFechar={fechar} />}
      {acao?.tipo === "entrada" && <ModalEntrada produto={acao.produto} aoFechar={fechar} />}
      {acao?.tipo === "ajuste" && <ModalAjuste produto={acao.produto} aoFechar={fechar} />}
      {acao?.tipo === "consumo" && <ModalConsumo produto={acao.produto} aoFechar={fechar} />}
      {acao?.tipo === "historico" && <ModalHistorico produto={acao.produto} aoFechar={() => setAcao(null)} />}
    </div>
  );
}

function BotaoLink({ children, onClick, cinza, vermelho }: { children: React.ReactNode; onClick: () => void; cinza?: boolean; vermelho?: boolean }) {
  const cor = vermelho
    ? "text-red-600 dark:text-red-400"
    : cinza
      ? "text-gray-600 dark:text-neutral-300"
      : "text-marca-primaria dark:text-marca-acento";
  return (
    <button type="button" className={`${cor} hover:underline`} onClick={onClick}>
      {children}
    </button>
  );
}

/** Formulário com envio, erro e "salvando" — o mesmo esqueleto dos quatro modais. */
function useEnvio(aoFechar: (mudou: boolean) => void) {
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function enviar(evento: FormEvent, acao: () => Promise<unknown>) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await acao();
      aoFechar(true);
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível salvar.");
    } finally {
      setEnviando(false);
    }
  }

  return { erro, enviando, enviar };
}

function Rodape({ enviando, aoFechar, rotulo }: { enviando: boolean; aoFechar: () => void; rotulo: string }) {
  return (
    <div className="flex justify-end gap-2 pt-2">
      <button type="button" className={classeBotaoSecundario} onClick={aoFechar}>
        Cancelar
      </button>
      <button type="submit" disabled={enviando} className={classeBotaoPrimario}>
        {enviando ? "Salvando..." : rotulo}
      </button>
    </div>
  );
}

function ModalProduto({ aberto, produto, aoFechar }: { aberto: boolean; produto: ProdutoResumo | null; aoFechar: (mudou: boolean) => void }) {
  const { chamarApi } = useAutenticacao();
  const { erro, enviando, enviar } = useEnvio(aoFechar);
  const [nome, setNome] = useState("");
  const [categoria, setCategoria] = useState("");
  const [precoCusto, setPrecoCusto] = useState("");
  const [precoVenda, setPrecoVenda] = useState("");
  const [quantidadeInicial, setQuantidadeInicial] = useState("0");
  const [quantidadeMinima, setQuantidadeMinima] = useState("3");

  useEffect(() => {
    if (!aberto) return;
    // Recomeça a cada abertura, com os dados do produto quando é edição.
    /* eslint-disable react-hooks/set-state-in-effect */
    setNome(produto?.nome ?? "");
    setCategoria(produto?.categoria ?? "");
    setPrecoCusto(produto ? String(produto.precoCusto) : "");
    setPrecoVenda(produto ? String(produto.precoVenda) : "");
    setQuantidadeInicial("0");
    setQuantidadeMinima(String(produto?.quantidadeMinima ?? 3));
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [aberto, produto]);

  const corpo = {
    nome,
    categoria: categoria || null,
    precoCusto: Number(precoCusto),
    precoVenda: Number(precoVenda),
    quantidadeMinima: Number(quantidadeMinima),
  };

  return (
    <Modal titulo={produto ? `Editar ${produto.nome}` : "Novo produto"} aberto={aberto} aoFechar={() => aoFechar(false)}>
      <form
        className="space-y-3"
        onSubmit={(e) =>
          enviar(e, () =>
            produto
              ? chamarApi(`/painel/estoque/produtos/${produto.id}`, { metodo: "PUT", corpo })
              : chamarApi("/painel/estoque/produtos", { metodo: "POST", corpo: { ...corpo, quantidadeInicial: Number(quantidadeInicial) } }),
          )
        }
      >
        <label className="block">
          <span className={classeLabel}>Nome</span>
          <input required className={classeInput} value={nome} onChange={(e) => setNome(e.target.value)} />
        </label>
        <label className="block">
          <span className={classeLabel}>Categoria (opcional)</span>
          <input className={classeInput} value={categoria} onChange={(e) => setCategoria(e.target.value)} placeholder="Cabelo" />
        </label>
        <div className="grid grid-cols-2 gap-3">
          <label className="block">
            <span className={classeLabel}>Preço de custo (R$)</span>
            <input required type="number" min={0} step="0.01" className={classeInput} value={precoCusto} onChange={(e) => setPrecoCusto(e.target.value)} />
          </label>
          <label className="block">
            <span className={classeLabel}>Preço de venda (R$)</span>
            <input required type="number" min={0} step="0.01" className={classeInput} value={precoVenda} onChange={(e) => setPrecoVenda(e.target.value)} />
          </label>
          {!produto && (
            <label className="block">
              <span className={classeLabel}>Quantidade inicial</span>
              <input required type="number" min={0} step={1} className={classeInput} value={quantidadeInicial} onChange={(e) => setQuantidadeInicial(e.target.value)} />
            </label>
          )}
          <label className="block">
            <span className={classeLabel}>Alerta com (quantidade mínima)</span>
            <input required type="number" min={0} step={1} className={classeInput} value={quantidadeMinima} onChange={(e) => setQuantidadeMinima(e.target.value)} />
          </label>
        </div>
        {produto && (
          <p className="text-xs text-gray-500 dark:text-neutral-400">A quantidade em estoque muda só por entrada ou ajuste, para o histórico ficar completo.</p>
        )}
        {erro && <p className="text-sm text-red-600">{erro}</p>}
        <Rodape enviando={enviando} aoFechar={() => aoFechar(false)} rotulo={produto ? "Salvar" : "Criar"} />
      </form>
    </Modal>
  );
}

function ModalEntrada({ produto, aoFechar }: { produto: ProdutoResumo; aoFechar: (mudou: boolean) => void }) {
  const { chamarApi } = useAutenticacao();
  const { erro, enviando, enviar } = useEnvio(aoFechar);
  const [quantidade, setQuantidade] = useState("");
  const [custo, setCusto] = useState(String(produto.precoCusto));
  const [fornecedor, setFornecedor] = useState("");
  const [observacao, setObservacao] = useState("");

  return (
    <Modal titulo={`Entrada — ${produto.nome}`} aberto aoFechar={() => aoFechar(false)}>
      <form
        className="space-y-3"
        onSubmit={(e) =>
          enviar(e, () =>
            chamarApi(`/painel/estoque/produtos/${produto.id}/entradas`, {
              metodo: "POST",
              corpo: { quantidade: Number(quantidade), custoUnitario: Number(custo), fornecedor: fornecedor || null, observacao: observacao || null },
            }),
          )
        }
      >
        <p className="text-sm text-gray-600 dark:text-neutral-400">Em estoque agora: {produto.quantidadeEstoque}</p>
        <div className="grid grid-cols-2 gap-3">
          <label className="block">
            <span className={classeLabel}>Quantidade</span>
            <input required autoFocus type="number" min={1} step={1} className={classeInput} value={quantidade} onChange={(e) => setQuantidade(e.target.value)} />
          </label>
          <label className="block">
            <span className={classeLabel}>Custo unitário (R$)</span>
            <input required type="number" min={0} step="0.01" className={classeInput} value={custo} onChange={(e) => setCusto(e.target.value)} />
          </label>
        </div>
        <label className="block">
          <span className={classeLabel}>Fornecedor (opcional)</span>
          <input className={classeInput} value={fornecedor} onChange={(e) => setFornecedor(e.target.value)} />
        </label>
        <label className="block">
          <span className={classeLabel}>Observação (opcional)</span>
          <input className={classeInput} value={observacao} onChange={(e) => setObservacao(e.target.value)} />
        </label>
        {erro && <p className="text-sm text-red-600">{erro}</p>}
        <Rodape enviando={enviando} aoFechar={() => aoFechar(false)} rotulo="Lançar entrada" />
      </form>
    </Modal>
  );
}

/**
 * Consumo interno (seção 7): produto usado pelo profissional. Baixa o estoque e vira saldo devedor dele, pelo preço de
 * custo (editável na hora) — não entra no faturamento. É descontado da comissão no fechamento da quinzena.
 */
function ModalConsumo({ produto, aoFechar }: { produto: ProdutoResumo; aoFechar: (mudou: boolean) => void }) {
  const { chamarApi } = useAutenticacao();
  const { erro, enviando, enviar } = useEnvio(aoFechar);
  const [profissionais, setProfissionais] = useState<SaldoDoProfissional[]>([]);
  const [pessoa, setPessoa] = useState("");
  const [quantidade, setQuantidade] = useState("1");
  const [valorUnitario, setValorUnitario] = useState(produto.precoCusto.toFixed(2));
  const [observacao, setObservacao] = useState("");

  useEffect(() => {
    // Busca disparada pela abertura: quem pode receber o consumo (profissionais ativos e vendedores com acerto por quinzena).
    chamarApi<SaldoDoProfissional[]>("/painel/saldos")
      .then((lista) => setProfissionais(lista.filter((p) => p.ativo)))
      .catch(() => setProfissionais([]));
  }, [chamarApi]);

  const total = (Number(quantidade) || 0) * (Number(valorUnitario) || 0);

  return (
    <Modal titulo={`Consumo interno — ${produto.nome}`} aberto aoFechar={() => aoFechar(false)}>
      <form
        className="space-y-3"
        onSubmit={(e) =>
          enviar(e, () =>
            chamarApi(`/painel/estoque/produtos/${produto.id}/consumos`, {
              metodo: "POST",
              corpo: { ...corpoPessoa(pessoa), quantidade: Number(quantidade), valorUnitario: Number(valorUnitario), observacao: observacao || null },
            }),
          )
        }
      >
        <p className="text-sm text-gray-600 dark:text-neutral-400">
          Em estoque agora: {produto.quantidadeEstoque}. O valor vira saldo devedor do profissional e é descontado da comissão dele.
        </p>
        <SeletorPessoaSaldo pessoas={profissionais} valor={pessoa} aoMudar={setPessoa} />
        <div className="grid grid-cols-2 gap-3">
          <label className="block">
            <span className={classeLabel}>Quantidade</span>
            <input required type="number" min={1} step={1} className={classeInput} value={quantidade} onChange={(e) => setQuantidade(e.target.value)} />
          </label>
          <label className="block">
            <span className={classeLabel}>Valor por unidade (R$)</span>
            <input required type="number" min={0} step="0.01" className={classeInput} value={valorUnitario} onChange={(e) => setValorUnitario(e.target.value)} />
          </label>
        </div>
        <p className="text-xs text-gray-500 dark:text-neutral-400">Sugerido: o preço de custo ({formatarReais(produto.precoCusto)}).</p>
        <label className="block">
          <span className={classeLabel}>Observação (opcional)</span>
          <input className={classeInput} value={observacao} onChange={(e) => setObservacao(e.target.value)} />
        </label>
        <p className="text-right text-sm font-medium text-gray-900 dark:text-neutral-50">Saldo devedor: {formatarReais(total)}</p>
        {erro && <p className="text-sm text-red-600">{erro}</p>}
        <Rodape enviando={enviando} aoFechar={() => aoFechar(false)} rotulo="Lançar consumo" />
      </form>
    </Modal>
  );
}

function ModalAjuste({ produto, aoFechar }: { produto: ProdutoResumo; aoFechar: (mudou: boolean) => void }) {
  const { chamarApi } = useAutenticacao();
  const { erro, enviando, enviar } = useEnvio(aoFechar);
  const [contada, setContada] = useState(String(produto.quantidadeEstoque));
  const [motivo, setMotivo] = useState("");

  return (
    <Modal titulo={`Ajuste — ${produto.nome}`} aberto aoFechar={() => aoFechar(false)}>
      <form
        className="space-y-3"
        onSubmit={(e) =>
          enviar(e, () =>
            chamarApi(`/painel/estoque/produtos/${produto.id}/ajustes`, {
              metodo: "POST",
              corpo: { quantidadeContada: Number(contada), motivo },
            }),
          )
        }
      >
        <p className="text-sm text-gray-600 dark:text-neutral-400">
          No sistema: {produto.quantidadeEstoque}. Informe quanto há de fato (perda, quebra, balanço).
        </p>
        <label className="block">
          <span className={classeLabel}>Quantidade contada</span>
          <input required autoFocus type="number" min={0} step={1} className={classeInput} value={contada} onChange={(e) => setContada(e.target.value)} />
        </label>
        <label className="block">
          <span className={classeLabel}>Motivo (obrigatório)</span>
          <input required className={classeInput} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        </label>
        {erro && <p className="text-sm text-red-600">{erro}</p>}
        <Rodape enviando={enviando} aoFechar={() => aoFechar(false)} rotulo="Ajustar" />
      </form>
    </Modal>
  );
}

function ModalHistorico({ produto, aoFechar }: { produto: ProdutoResumo; aoFechar: () => void }) {
  const { chamarApi } = useAutenticacao();
  const [movimentos, setMovimentos] = useState<MovimentoEstoqueResumo[] | null>(null);

  useEffect(() => {
    chamarApi<MovimentoEstoqueResumo[]>(`/painel/estoque/produtos/${produto.id}/movimentos`).then(setMovimentos);
  }, [chamarApi, produto.id]);

  return (
    <Modal titulo={`Histórico — ${produto.nome}`} aberto aoFechar={aoFechar}>
      {!movimentos ? (
        <p className="text-sm text-gray-500 dark:text-neutral-400">Carregando...</p>
      ) : (
        <ul className="divide-y divide-gray-100 text-sm dark:divide-neutral-800">
          {movimentos.map((m) => (
            <li key={m.id} className="py-2">
              <div className="flex justify-between gap-2">
                <span className="font-medium text-gray-900 dark:text-neutral-50">
                  {ROTULO_MOVIMENTO[m.tipo]} {m.quantidade > 0 ? `+${m.quantidade}` : m.quantidade}
                </span>
                <span className="text-gray-500 dark:text-neutral-400">{new Date(m.data).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" })}</span>
              </div>
              <p className="text-xs text-gray-500 dark:text-neutral-400">
                {m.quantidadeAntes} → {m.quantidadeDepois}
                {m.valorUnitario !== null && ` · ${formatarReais(m.valorUnitario)} cada`}
                {m.fornecedor && ` · ${m.fornecedor}`}
                {m.usuario && ` · por ${m.usuario}`}
              </p>
              {m.observacao && <p className="text-xs text-gray-600 dark:text-neutral-300">{m.observacao}</p>}
            </li>
          ))}
        </ul>
      )}
    </Modal>
  );
}
