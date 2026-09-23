"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import type { ClienteResumo, ExportacaoCliente, ProgressoFidelidade } from "@/lib/tipos";

function baixarJson(nomeArquivo: string, dados: unknown) {
  const blob = new Blob([JSON.stringify(dados, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = nomeArquivo;
  link.click();
  URL.revokeObjectURL(url);
}

export default function PaginaClientes() {
  const { chamarApi } = useAutenticacao();
  const [clientes, setClientes] = useState<ClienteResumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [modalAberto, setModalAberto] = useState(false);
  const [clienteEditando, setClienteEditando] = useState<ClienteResumo | null>(null);
  const [clienteFidelidade, setClienteFidelidade] = useState<ClienteResumo | null>(null);

  const carregar = useCallback(async () => {
    try {
      setClientes(await chamarApi<ClienteResumo[]>("/painel/clientes"));
    } catch {
      setErro("Não foi possível carregar os clientes.");
    }
  }, [chamarApi]);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  async function exportarDados(cliente: ClienteResumo) {
    const exportacao = await chamarApi<ExportacaoCliente>(`/painel/clientes/${cliente.id}/exportar`);
    baixarJson(`dados-${cliente.nome.toLowerCase().replace(/\s+/g, "-")}.json`, exportacao);
  }

  async function excluirDados(cliente: ClienteResumo) {
    if (!confirm(`Excluir os dados pessoais de "${cliente.nome}"? O histórico de agendamentos é mantido, mas anonimizado — isso não pode ser desfeito.`)) {
      return;
    }
    await chamarApi(`/painel/clientes/${cliente.id}/excluir`, { metodo: "POST" });
    await carregar();
  }

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Clientes</h1>
        <button className={classeBotaoPrimario} onClick={() => setModalAberto(true)}>
          Novo cliente
        </button>
      </div>

      {erro && <p className="mb-4 text-sm text-red-600">{erro}</p>}

      <div className={classeCartao}>
        <div className="overflow-x-auto">
          <table className="w-full min-w-137.5">
            <thead className="border-b border-gray-200 dark:border-neutral-800">
              <tr>
                <th className={classeTh}>Nome</th>
                <th className={classeTh}>Telefone</th>
                <th className={classeTh}>E-mail</th>
                <th className={classeTh}>Ações</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 dark:divide-neutral-800">
              {clientes?.map((cliente) => (
                <tr key={cliente.id} className={cliente.excluido ? "opacity-50" : ""}>
                  <td className={classeTd}>{cliente.nome}</td>
                  <td className={classeTd}>{cliente.telefone}</td>
                  <td className={classeTd}>{cliente.email ?? "—"}</td>
                  <td className={`${classeTd} space-x-3`}>
                    {cliente.excluido ? (
                      <span className="text-xs text-gray-400">Dados excluídos</span>
                    ) : (
                      <>
                        <button className="text-marca-primaria hover:underline dark:text-marca-acento" onClick={() => setClienteEditando(cliente)}>
                          Editar
                        </button>
                        <button className="text-gray-600 hover:underline dark:text-neutral-300" onClick={() => setClienteFidelidade(cliente)}>
                          Fidelidade
                        </button>
                        <button className="text-gray-600 hover:underline dark:text-neutral-300" onClick={() => exportarDados(cliente)}>
                          Exportar dados
                        </button>
                        <button className="text-red-600 hover:underline dark:text-red-400" onClick={() => excluirDados(cliente)}>
                          Excluir dados
                        </button>
                      </>
                    )}
                  </td>
                </tr>
              ))}
              {clientes?.length === 0 && (
                <tr>
                  <td className={classeTd} colSpan={4}>
                    Nenhum cliente cadastrado ainda.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      <ModalCliente
        titulo="Novo cliente"
        aberto={modalAberto}
        aoFechar={() => setModalAberto(false)}
        aoSalvar={async () => {
          setModalAberto(false);
          await carregar();
        }}
      />

      {clienteEditando && (
        <ModalCliente
          titulo="Editar cliente"
          aberto
          cliente={clienteEditando}
          aoFechar={() => setClienteEditando(null)}
          aoSalvar={async () => {
            setClienteEditando(null);
            await carregar();
          }}
        />
      )}

      {clienteFidelidade && <ModalFidelidade cliente={clienteFidelidade} aoFechar={() => setClienteFidelidade(null)} />}
    </div>
  );
}

function ModalFidelidade({ cliente, aoFechar }: { cliente: ClienteResumo; aoFechar: () => void }) {
  const { chamarApi } = useAutenticacao();
  const [progresso, setProgresso] = useState<ProgressoFidelidade | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [resgatando, setResgatando] = useState(false);

  const carregarProgresso = useCallback(async () => {
    try {
      setErro(null);
      setProgresso(await chamarApi<ProgressoFidelidade>(`/painel/fidelidade/clientes/${cliente.id}/progresso`));
    } catch {
      setErro("Não foi possível carregar a fidelidade (o negócio talvez ainda não tenha um programa configurado em Meu negócio → Fidelidade).");
    }
  }, [chamarApi, cliente.id]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregarProgresso();
  }, [carregarProgresso]);

  async function resgatar() {
    setResgatando(true);
    try {
      await chamarApi(`/painel/fidelidade/clientes/${cliente.id}/resgatar`, { metodo: "POST" });
      await carregarProgresso();
    } catch {
      setErro("Não foi possível resgatar.");
    } finally {
      setResgatando(false);
    }
  }

  return (
    <Modal titulo={`Fidelidade — ${cliente.nome}`} aberto aoFechar={aoFechar}>
      {erro && <p className="text-sm text-red-600">{erro}</p>}
      {progresso && !erro && (
        <div className="space-y-3">
          <p className="text-sm text-gray-700 dark:text-neutral-300">
            {progresso.selosNecessarios > 0
              ? `${progresso.selosAtuais} de ${progresso.selosNecessarios} selos`
              : `${progresso.selosAtuais} selo(s) — nenhum programa ativo no momento`}
          </p>
          {progresso.descricaoRecompensa && (
            <p className="text-xs text-gray-500 dark:text-neutral-400">Recompensa: {progresso.descricaoRecompensa}</p>
          )}
          {progresso.podeResgatar && (
            <button disabled={resgatando} className={classeBotaoPrimario} onClick={resgatar}>
              {resgatando ? "Resgatando..." : "Resgatar recompensa"}
            </button>
          )}
        </div>
      )}
    </Modal>
  );
}

function ModalCliente({
  titulo,
  aberto,
  cliente,
  aoFechar,
  aoSalvar,
}: {
  titulo: string;
  aberto: boolean;
  cliente?: ClienteResumo;
  aoFechar: () => void;
  aoSalvar: () => Promise<void>;
}) {
  const { chamarApi } = useAutenticacao();
  const [nome, setNome] = useState(cliente?.nome ?? "");
  const [telefone, setTelefone] = useState(cliente?.telefone ?? "");
  const [email, setEmail] = useState(cliente?.email ?? "");
  const [observacoes, setObservacoes] = useState(cliente?.observacoes ?? "");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      if (cliente) {
        await chamarApi(`/painel/clientes/${cliente.id}`, {
          metodo: "PUT",
          corpo: { nome, email: email || null, observacoes: observacoes || null },
        });
      } else {
        await chamarApi("/painel/clientes", {
          metodo: "POST",
          corpo: { nome, telefone, email: email || null, observacoes: observacoes || null },
        });
      }
      await aoSalvar();
    } catch {
      setErro(cliente ? "Não foi possível salvar as alterações." : "Não foi possível criar o cliente. Confira se o telefone já não está cadastrado.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo={titulo} aberto={aberto} aoFechar={aoFechar}>
      <form onSubmit={aoEnviar} className="space-y-3">
        <label>
          <span className={classeLabel}>Nome</span>
          <input required className={classeInput} value={nome} onChange={(e) => setNome(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Telefone (E.164)</span>
          <input
            required
            disabled={!!cliente}
            className={`${classeInput} disabled:opacity-60`}
            value={telefone}
            onChange={(e) => setTelefone(e.target.value)}
            placeholder="+5571988887777"
          />
        </label>
        <label>
          <span className={classeLabel}>E-mail (opcional)</span>
          <input type="email" className={classeInput} value={email ?? ""} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Observações (opcional)</span>
          <textarea className={classeInput} rows={3} value={observacoes ?? ""} onChange={(e) => setObservacoes(e.target.value)} />
        </label>

        {erro && <p className="text-sm text-red-600">{erro}</p>}

        <div className="flex justify-end gap-2 pt-2">
          <button type="button" className={classeBotaoSecundario} onClick={aoFechar}>
            Cancelar
          </button>
          <button type="submit" disabled={enviando} className={classeBotaoPrimario}>
            {enviando ? "Salvando..." : "Salvar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
