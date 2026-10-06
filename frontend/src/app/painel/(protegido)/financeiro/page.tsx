"use client";

import { useCallback, useEffect, useState } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { CabecalhoOperacional, IndicadorOperacional } from "@/components/painel/OperacionaisVisuais";
import { classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import type { ProfissionalResumo, ResumoFinanceiro, ServicoResumo } from "@/lib/tipos";
import { dataLocalIso, formatarReais } from "@/lib/formatacao";

function primeiroDiaDoMes(): string {
  const hoje = new Date();
  return dataLocalIso(new Date(hoje.getFullYear(), hoje.getMonth(), 1));
}

function ultimoDiaDoMes(): string {
  const hoje = new Date();
  return dataLocalIso(new Date(hoje.getFullYear(), hoje.getMonth() + 1, 0));
}

export default function PaginaFinanceiro() {
  const { chamarApi } = useAutenticacao();

  const [inicio, setInicio] = useState(primeiroDiaDoMes());
  const [fim, setFim] = useState(ultimoDiaDoMes());
  const [profissionalId, setProfissionalId] = useState("");
  const [servicoId, setServicoId] = useState("");

  const [profissionais, setProfissionais] = useState<ProfissionalResumo[]>([]);
  const [servicos, setServicos] = useState<ServicoResumo[]>([]);
  const [resumo, setResumo] = useState<ResumoFinanceiro | null>(null);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    Promise.all([
      chamarApi<ProfissionalResumo[]>("/painel/profissionais"),
      chamarApi<ServicoResumo[]>("/painel/servicos"),
    ]).then(([listaProfissionais, listaServicos]) => {
      setProfissionais(listaProfissionais);
      setServicos(listaServicos);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const carregarResumo = useCallback(async () => {
    try {
      setErro(null);
      const parametros = new URLSearchParams({ inicio, fim });
      if (profissionalId) parametros.set("profissionalId", profissionalId);
      if (servicoId) parametros.set("servicoId", servicoId);
      setResumo(await chamarApi<ResumoFinanceiro>(`/painel/financeiro/resumo?${parametros.toString()}`));
    } catch {
      setErro("Não foi possível carregar o resumo financeiro.");
    }
  }, [chamarApi, inicio, fim, profissionalId, servicoId]);

  useEffect(() => {
    // Busca disparada pela troca dos filtros acima.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregarResumo();
  }, [carregarResumo]);

  return (
    <div className="painel-operacional">
      <CabecalhoOperacional titulo="Financeiro" descricao="Acompanhe o faturamento de serviços e produtos no período." />

      <div className="painel-operacional-toolbar">
        <label>
          <span className={classeLabel}>De</span>
          <input type="date" className={classeInput} value={inicio} onChange={(e) => setInicio(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Até</span>
          <input type="date" className={classeInput} value={fim} onChange={(e) => setFim(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Profissional</span>
          <select className={classeInput} value={profissionalId} onChange={(e) => setProfissionalId(e.target.value)}>
            <option value="">Todos</option>
            {profissionais.map((p) => (
              <option key={p.id} value={p.id}>
                {p.nome}
              </option>
            ))}
          </select>
        </label>
        <label>
          <span className={classeLabel}>Serviço</span>
          <select className={classeInput} value={servicoId} onChange={(e) => setServicoId(e.target.value)}>
            <option value="">Todos</option>
            {servicos.map((s) => (
              <option key={s.id} value={s.id}>
                {s.nome}
              </option>
            ))}
          </select>
        </label>
      </div>

      {erro && <p className="text-sm text-red-600">{erro}</p>}
      {!resumo && !erro && <p role="status" className="painel-operacional-carregando">Carregando...</p>}

      {resumo && (
        <>
          {/* Serviços e produtos sempre separados, além do total (seção 7). */}
          <div className="painel-operacional-indicadores">
            <IndicadorOperacional rotulo="Faturamento no período" valor={formatarReais(resumo.total)} nomeAcessivel="Faturamento total" destaque />
            <IndicadorOperacional rotulo={<>Serviços · {resumo.quantidadeAtendimentos} atendimento(s) pago(s)</>} valor={formatarReais(resumo.totalServicos)} nomeAcessivel="Faturamento de serviços" />
            <IndicadorOperacional rotulo={<>Produtos · {resumo.quantidadeVendas} venda(s)</>} valor={formatarReais(resumo.totalProdutos)} nomeAcessivel="Faturamento de produtos" />
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <section>
              <h2 className="mb-2 text-sm font-semibold tracking-wide text-gray-500 uppercase dark:text-neutral-400">
                Por profissional
              </h2>
              <div className={classeCartao}>
                <table role="table" aria-label="Faturamento por profissional" className="painel-operacional-cards w-full">
                  <thead role="rowgroup" className="border-b border-gray-200 dark:border-neutral-800">
                    <tr role="row">
                      <th role="columnheader" scope="col" data-label="Profissional" className={classeTh}>Profissional</th>
                      <th role="columnheader" scope="col" data-label="Atendimentos" className={classeTh}>Atendimentos</th>
                      <th role="columnheader" scope="col" data-label="Total" className={classeTh}>Total</th>
                    </tr>
                  </thead>
                  <tbody role="rowgroup" className="divide-y divide-gray-100 dark:divide-neutral-800">
                    {resumo.porProfissional.map((linha) => (
                      <tr role="row" key={linha.profissionalId}>
                        <td role="cell" data-label="Profissional" className={classeTd}>{linha.nomeProfissional}</td>
                        <td role="cell" data-label="Atendimentos" className={classeTd}>{linha.quantidade}</td>
                        <td role="cell" data-label="Total" className={classeTd}>{formatarReais(linha.total)}</td>
                      </tr>
                    ))}
                    {resumo.porProfissional.length === 0 && (
                      <tr role="row">
                        <td role="cell" className={classeTd} colSpan={3}>
                          Nenhum pagamento no período.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </section>

            <section>
              <h2 className="mb-2 text-sm font-semibold tracking-wide text-gray-500 uppercase dark:text-neutral-400">Por serviço</h2>
              <div className={classeCartao}>
                <table role="table" aria-label="Faturamento por serviço" className="painel-operacional-cards w-full">
                  <thead role="rowgroup" className="border-b border-gray-200 dark:border-neutral-800">
                    <tr role="row">
                      <th role="columnheader" scope="col" data-label="Serviço" className={classeTh}>Serviço</th>
                      <th role="columnheader" scope="col" data-label="Qtd." className={classeTh}>Qtd.</th>
                      <th role="columnheader" scope="col" data-label="Total" className={classeTh}>Total</th>
                    </tr>
                  </thead>
                  <tbody role="rowgroup" className="divide-y divide-gray-100 dark:divide-neutral-800">
                    {resumo.porServico.map((linha) => (
                      <tr role="row" key={linha.servicoId}>
                        <td role="cell" data-label="Serviço" className={classeTd}>{linha.nomeServico}</td>
                        <td role="cell" data-label="Qtd." className={classeTd}>{linha.quantidade}</td>
                        <td role="cell" data-label="Total" className={classeTd}>{formatarReais(linha.total)}</td>
                      </tr>
                    ))}
                    {resumo.porServico.length === 0 && (
                      <tr role="row">
                        <td role="cell" className={classeTd} colSpan={3}>
                          Nenhum pagamento no período.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </section>

            {resumo.porProduto.length > 0 && (
              <section>
                <h2 className="mb-2 text-sm font-semibold tracking-wide text-gray-500 uppercase dark:text-neutral-400">Por produto</h2>
                <div className={classeCartao}>
                  <table role="table" aria-label="Faturamento por produto" className="painel-operacional-cards w-full">
                    <thead role="rowgroup" className="border-b border-gray-200 dark:border-neutral-800">
                      <tr role="row">
                        <th role="columnheader" scope="col" data-label="Produto" className={classeTh}>Produto</th>
                        <th role="columnheader" scope="col" data-label="Qtd." className={classeTh}>Qtd.</th>
                        <th role="columnheader" scope="col" data-label="Total" className={classeTh}>Total</th>
                      </tr>
                    </thead>
                    <tbody role="rowgroup" className="divide-y divide-gray-100 dark:divide-neutral-800">
                      {resumo.porProduto.map((linha) => (
                        <tr role="row" key={linha.produtoId}>
                          <td role="cell" data-label="Produto" className={classeTd}>{linha.nomeProduto}</td>
                          <td role="cell" data-label="Qtd." className={classeTd}>{linha.quantidade}</td>
                          <td role="cell" data-label="Total" className={classeTd}>{formatarReais(linha.total)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </section>
            )}
          </div>
        </>
      )}
    </div>
  );
}
