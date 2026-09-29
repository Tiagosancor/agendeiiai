"use client";

import { classeInput, classeLabel } from "@/components/estilos";
import type { ItemLancarVenda, OpcoesVenda, VendedorOpcao } from "@/lib/tipos";
import { formatarReais } from "@/lib/formatacao";

/** Uma linha em edição: valores como texto (inputs); `chave` só identifica a linha na tela. */
export interface LinhaVenda {
  chave: string;
  produtoId: string;
  quantidade: string;
  valorUnitario: string;
}

/** "p:{id}" (profissional) ou "u:{id}" (usuário sem cadastro de profissional). */
export function chaveVendedor(vendedor: VendedorOpcao | null | undefined): string {
  if (!vendedor) return "";
  return vendedor.profissionalId ? `p:${vendedor.profissionalId}` : `u:${vendedor.usuarioId}`;
}

export function corpoVendedor(chave: string) {
  return {
    vendedorProfissionalId: chave.startsWith("p:") ? chave.slice(2) : null,
    vendedorUsuarioId: chave.startsWith("u:") ? chave.slice(2) : null,
  };
}

export function itensDaVenda(linhas: LinhaVenda[]): ItemLancarVenda[] {
  return linhas
    .filter((l) => l.produtoId)
    .map((l) => ({ produtoId: l.produtoId, quantidade: Number(l.quantidade), valorUnitario: Number(l.valorUnitario) }));
}

export function totalDaVenda(linhas: LinhaVenda[]): number {
  return itensDaVenda(linhas).reduce((soma, i) => soma + (i.quantidade || 0) * (i.valorUnitario || 0), 0);
}

/**
 * Produtos da venda e "Vendido por" (seção 7): o preço de venda vem sugerido e pode ser editado na hora (desconto
 * pontual); o vendedor vem sugerido, mas é sempre escolhido explicitamente — uma pessoa só por venda.
 */
export function CamposVenda({
  opcoes,
  linhas,
  aoMudarLinhas,
  vendedor,
  aoMudarVendedor,
}: {
  opcoes: OpcoesVenda;
  linhas: LinhaVenda[];
  aoMudarLinhas: (linhas: LinhaVenda[]) => void;
  vendedor: string;
  aoMudarVendedor: (chave: string) => void;
}) {
  function mudar(chave: string, campo: Partial<LinhaVenda>) {
    aoMudarLinhas(linhas.map((l) => (l.chave === chave ? { ...l, ...campo } : l)));
  }

  function escolherProduto(chave: string, produtoId: string) {
    const produto = opcoes.produtos.find((p) => p.id === produtoId);
    mudar(chave, { produtoId, valorUnitario: produto ? produto.precoVenda.toFixed(2) : "" });
  }

  const escolhidos = new Set(linhas.map((l) => l.produtoId));

  return (
    <div className="space-y-3">
      {linhas.map((linha, indice) => {
        const produto = opcoes.produtos.find((p) => p.id === linha.produtoId);
        const acimaDoEstoque = produto && Number(linha.quantidade) > produto.quantidadeEstoque;
        return (
          <div key={linha.chave} className="rounded-lg border border-gray-200 p-3 dark:border-neutral-800" role="group" aria-label={`Produto ${indice + 1}`}>
            <div className="grid gap-2 sm:grid-cols-[1fr_6rem_8rem]">
              <label>
                <span className={classeLabel}>Produto</span>
                <select className={classeInput} value={linha.produtoId} onChange={(e) => escolherProduto(linha.chave, e.target.value)}>
                  <option value="">Escolha...</option>
                  {opcoes.produtos.map((p) => (
                    <option key={p.id} value={p.id} disabled={p.quantidadeEstoque === 0 || (escolhidos.has(p.id) && p.id !== linha.produtoId)}>
                      {p.nome} ({p.quantidadeEstoque === 0 ? "esgotado" : `${p.quantidadeEstoque} em estoque`})
                    </option>
                  ))}
                </select>
              </label>
              <label>
                <span className={classeLabel}>Qtd.</span>
                <input
                  type="number"
                  min={1}
                  step={1}
                  className={classeInput}
                  value={linha.quantidade}
                  onChange={(e) => mudar(linha.chave, { quantidade: e.target.value })}
                />
              </label>
              <label>
                <span className={classeLabel}>Valor unit. (R$)</span>
                <input
                  type="number"
                  min={0}
                  step="0.01"
                  className={classeInput}
                  value={linha.valorUnitario}
                  onChange={(e) => mudar(linha.chave, { valorUnitario: e.target.value })}
                />
              </label>
            </div>
            <div className="mt-1 flex items-center justify-between text-xs">
              {acimaDoEstoque ? (
                <span className="text-red-600">Só há {produto.quantidadeEstoque} em estoque.</span>
              ) : (
                <span className="text-gray-500 dark:text-neutral-400">
                  {produto && Number(linha.valorUnitario) !== produto.precoVenda ? `Preço de tabela: ${formatarReais(produto.precoVenda)}` : ""}
                </span>
              )}
              <button
                type="button"
                className="text-red-700 hover:underline dark:text-red-400"
                onClick={() => aoMudarLinhas(linhas.filter((l) => l.chave !== linha.chave))}
              >
                Remover
              </button>
            </div>
          </div>
        );
      })}

      <button
        type="button"
        className="text-sm text-marca-primaria hover:underline dark:text-marca-acento"
        onClick={() => aoMudarLinhas([...linhas, { chave: crypto.randomUUID(), produtoId: "", quantidade: "1", valorUnitario: "" }])}
      >
        + Adicionar produto
      </button>

      {linhas.length > 0 && (
        <>
          <label className="block">
            <span className={classeLabel}>Vendido por</span>
            <select className={classeInput} value={vendedor} onChange={(e) => aoMudarVendedor(e.target.value)}>
              <option value="">Escolha quem vendeu...</option>
              {opcoes.vendedores.map((v) => (
                <option key={chaveVendedor(v)} value={chaveVendedor(v)}>
                  {v.nome}
                </option>
              ))}
            </select>
          </label>
          <p className="text-right text-sm font-medium text-gray-900 dark:text-neutral-50">Total dos produtos: {formatarReais(totalDaVenda(linhas))}</p>
        </>
      )}
    </div>
  );
}
