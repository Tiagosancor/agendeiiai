"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import type { ProfissionalResumo } from "@/lib/tipos";

export default function PaginaProfissionais() {
  const { chamarApi } = useAutenticacao();
  const [profissionais, setProfissionais] = useState<ProfissionalResumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [modalAberto, setModalAberto] = useState(false);

  const carregar = useCallback(async () => {
    try {
      setProfissionais(await chamarApi<ProfissionalResumo[]>("/painel/profissionais"));
    } catch {
      setErro("Não foi possível carregar os profissionais.");
    }
  }, [chamarApi]);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  async function alternarAtivo(profissional: ProfissionalResumo) {
    const acao = profissional.ativo ? "desativar" : "ativar";
    await chamarApi(`/painel/profissionais/${profissional.id}/${acao}`, { metodo: "POST" });
    await carregar();
  }

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Profissionais</h1>
        <button className={classeBotaoPrimario} onClick={() => setModalAberto(true)}>
          Novo profissional
        </button>
      </div>

      {erro && <p className="mb-4 text-sm text-red-600">{erro}</p>}

      <div className={classeCartao}>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[500px]">
            <thead className="border-b border-gray-200 dark:border-neutral-800">
              <tr>
                <th className={classeTh}>Nome</th>
                <th className={classeTh}>Status</th>
                <th className={classeTh}>Ações</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 dark:divide-neutral-800">
              {profissionais?.map((profissional) => (
                <tr key={profissional.id}>
                  <td className={classeTd}>{profissional.nome}</td>
                  <td className={classeTd}>{profissional.ativo ? "Ativo" : "Inativo"}</td>
                  <td className={classeTd}>
                    <button className="text-gray-600 hover:underline dark:text-neutral-300" onClick={() => alternarAtivo(profissional)}>
                      {profissional.ativo ? "Desativar" : "Ativar"}
                    </button>
                  </td>
                </tr>
              ))}
              {profissionais?.length === 0 && (
                <tr>
                  <td className={classeTd} colSpan={3}>
                    Nenhum profissional cadastrado ainda.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      <ModalCriarProfissional
        aberto={modalAberto}
        aoFechar={() => setModalAberto(false)}
        aoCriar={async () => {
          setModalAberto(false);
          await carregar();
        }}
      />
    </div>
  );
}

function ModalCriarProfissional({
  aberto,
  aoFechar,
  aoCriar,
}: {
  aberto: boolean;
  aoFechar: () => void;
  aoCriar: () => Promise<void>;
}) {
  const { chamarApi } = useAutenticacao();
  const [nome, setNome] = useState("");
  const [telefone, setTelefone] = useState("");
  const [email, setEmail] = useState("");
  const [cpf, setCpf] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await chamarApi("/painel/profissionais", {
        metodo: "POST",
        corpo: { nome, telefone: telefone || null, email: email || null, cpf: cpf || null },
      });
      setNome("");
      setTelefone("");
      setEmail("");
      setCpf("");
      await aoCriar();
    } catch {
      setErro("Não foi possível criar o profissional.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo="Novo profissional" aberto={aberto} aoFechar={aoFechar}>
      <form onSubmit={aoEnviar} className="space-y-3">
        <label>
          <span className={classeLabel}>Nome</span>
          <input required className={classeInput} value={nome} onChange={(e) => setNome(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Telefone (opcional)</span>
          <input className={classeInput} value={telefone} onChange={(e) => setTelefone(e.target.value)} placeholder="+5571988887777" />
        </label>
        <label>
          <span className={classeLabel}>E-mail (opcional)</span>
          <input type="email" className={classeInput} value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>CPF (opcional)</span>
          <input className={classeInput} value={cpf} onChange={(e) => setCpf(e.target.value)} placeholder="000.000.000-00" />
        </label>

        {erro && <p className="text-sm text-red-600">{erro}</p>}

        <div className="flex justify-end gap-2 pt-2">
          <button type="button" className={classeBotaoSecundario} onClick={aoFechar}>
            Cancelar
          </button>
          <button type="submit" disabled={enviando} className={classeBotaoPrimario}>
            {enviando ? "Criando..." : "Criar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
