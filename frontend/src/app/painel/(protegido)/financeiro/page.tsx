"use client";

import { useCallback, useEffect, useState } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import type { ProfissionalResumo, ResumoFinanceiro, ServicoResumo } from "@/lib/tipos";

function primeiroDiaDoMes(): string {
  const hoje = new Date();
  return new Date(hoje.getFullYear(), hoje.getMonth(), 1).toISOString().slice(0, 10);
}

function ultimoDiaDoMes(): string {
  const hoje = new Date();
  return new Date(hoje.getFullYear(), hoje.getMonth() + 1, 0).toISOString().slice(0, 10);
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
    <div className="space-y-6">
      <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Financeiro</h1>

      <div className="flex flex-wrap gap-3">
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

      {resumo && (
        <>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-2">
            <div className={`${classeCartao} p-4`}>
              <p className="text-xs text-gray-500 dark:text-neutral-400">Faturamento no período</p>
              <p className="text-2xl font-semibold text-gray-900 dark:text-neutral-50">R$ {resumo.total.toFixed(2)}</p>
            </div>
            <div className={`${classeCartao} p-4`}>
              <p className="text-xs text-gray-500 dark:text-neutral-400">Atendimentos pagos</p>
              <p className="text-2xl font-semibold text-gray-900 dark:text-neutral-50">{resumo.quantidadeAtendimentos}</p>
            </div>
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <section>
              <h2 className="mb-2 text-sm font-semibold tracking-wide text-gray-500 uppercase dark:text-neutral-400">
                Por profissional
              </h2>
              <div className={classeCartao}>
                <table className="w-full">
                  <thead className="border-b border-gray-200 dark:border-neutral-800">
                    <tr>
                      <th className={classeTh}>Profissional</th>
                      <th className={classeTh}>Atendimentos</th>
                      <th className={classeTh}>Total</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100 dark:divide-neutral-800">
                    {resumo.porProfissional.map((linha) => (
                      <tr key={linha.profissionalId}>
                        <td className={classeTd}>{linha.nomeProfissional}</td>
                        <td className={classeTd}>{linha.quantidade}</td>
                        <td className={classeTd}>R$ {linha.total.toFixed(2)}</td>
                      </tr>
                    ))}
                    {resumo.porProfissional.length === 0 && (
                      <tr>
                        <td className={classeTd} colSpan={3}>
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
                <table className="w-full">
                  <thead className="border-b border-gray-200 dark:border-neutral-800">
                    <tr>
                      <th className={classeTh}>Serviço</th>
                      <th className={classeTh}>Qtd.</th>
                      <th className={classeTh}>Total</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100 dark:divide-neutral-800">
                    {resumo.porServico.map((linha) => (
                      <tr key={linha.servicoId}>
                        <td className={classeTd}>{linha.nomeServico}</td>
                        <td className={classeTd}>{linha.quantidade}</td>
                        <td className={classeTd}>R$ {linha.total.toFixed(2)}</td>
                      </tr>
                    ))}
                    {resumo.porServico.length === 0 && (
                      <tr>
                        <td className={classeTd} colSpan={3}>
                          Nenhum pagamento no período.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </section>
          </div>
        </>
      )}
    </div>
  );
}
