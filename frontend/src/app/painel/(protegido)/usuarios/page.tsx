"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import { PERFIS, PERMISSOES, type Perfil, type Permissao, type UsuarioDetalhe, type UsuarioResumo } from "@/lib/tipos";

export default function PaginaUsuarios() {
  const { chamarApi } = useAutenticacao();

  const [usuarios, setUsuarios] = useState<UsuarioResumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [modalCriarAberto, setModalCriarAberto] = useState(false);
  const [usuarioPermissoes, setUsuarioPermissoes] = useState<UsuarioDetalhe | null>(null);

  const carregar = useCallback(async () => {
    try {
      setUsuarios(await chamarApi<UsuarioResumo[]>("/painel/usuarios"));
    } catch {
      setErro("Não foi possível carregar os usuários.");
    }
  }, [chamarApi]);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  async function alternarAtivo(usuario: UsuarioResumo) {
    const acao = usuario.ativo ? "desativar" : "ativar";
    await chamarApi(`/painel/usuarios/${usuario.id}/${acao}`, { metodo: "POST" });
    await carregar();
  }

  async function abrirPermissoes(id: string) {
    const detalhe = await chamarApi<UsuarioDetalhe>(`/painel/usuarios/${id}`);
    setUsuarioPermissoes(detalhe);
  }

  async function alternarPermissao(permissao: Permissao, concedida: boolean) {
    if (!usuarioPermissoes) return;
    const metodo = concedida ? "DELETE" : "POST";
    await chamarApi(`/painel/usuarios/${usuarioPermissoes.id}/permissoes/${permissao}`, { metodo });
    setUsuarioPermissoes(await chamarApi<UsuarioDetalhe>(`/painel/usuarios/${usuarioPermissoes.id}`));
    await carregar();
  }

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Usuários</h1>
        <button className={classeBotaoPrimario} onClick={() => setModalCriarAberto(true)}>
          Novo usuário
        </button>
      </div>

      {erro && <p className="mb-4 text-sm text-red-600">{erro}</p>}

      <div className={classeCartao}>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[600px]">
            <thead className="border-b border-gray-200 dark:border-neutral-800">
              <tr>
                <th className={classeTh}>Nome</th>
                <th className={classeTh}>E-mail</th>
                <th className={classeTh}>Perfil</th>
                <th className={classeTh}>Status</th>
                <th className={classeTh}>Ações</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 dark:divide-neutral-800">
              {usuarios?.map((usuario) => (
                <tr key={usuario.id}>
                  <td className={classeTd}>{usuario.nome}</td>
                  <td className={classeTd}>{usuario.email}</td>
                  <td className={classeTd}>{usuario.perfil}</td>
                  <td className={classeTd}>{usuario.ativo ? "Ativo" : "Inativo"}</td>
                  <td className={`${classeTd} space-x-3`}>
                    <button className="text-marca-primaria hover:underline dark:text-marca-acento" onClick={() => abrirPermissoes(usuario.id)}>
                      Permissões
                    </button>
                    <button className="text-gray-600 hover:underline dark:text-neutral-300" onClick={() => alternarAtivo(usuario)}>
                      {usuario.ativo ? "Desativar" : "Ativar"}
                    </button>
                  </td>
                </tr>
              ))}
              {usuarios?.length === 0 && (
                <tr>
                  <td className={classeTd} colSpan={5}>
                    Nenhum usuário cadastrado ainda.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      <ModalCriarUsuario
        aberto={modalCriarAberto}
        aoFechar={() => setModalCriarAberto(false)}
        aoCriar={async () => {
          setModalCriarAberto(false);
          await carregar();
        }}
      />

      <Modal titulo={`Permissões de ${usuarioPermissoes?.nome ?? ""}`} aberto={usuarioPermissoes !== null} aoFechar={() => setUsuarioPermissoes(null)}>
        {usuarioPermissoes && (
          <div className="space-y-2">
            <p className="text-sm text-gray-500 dark:text-neutral-400">
              Perfil {usuarioPermissoes.perfil} — o administrador pode ajustar cada permissão individualmente.
            </p>
            {PERMISSOES.map((permissao) => {
              const concedida = usuarioPermissoes.permissoes.includes(permissao.valor);
              return (
                <label key={permissao.valor} className="flex items-center gap-2 text-sm text-gray-700 dark:text-neutral-200">
                  <input
                    type="checkbox"
                    checked={concedida}
                    onChange={() => alternarPermissao(permissao.valor, concedida)}
                  />
                  {permissao.rotulo}
                </label>
              );
            })}
          </div>
        )}
      </Modal>
    </div>
  );
}

function ModalCriarUsuario({
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
  const [email, setEmail] = useState("");
  const [senha, setSenha] = useState("");
  const [perfil, setPerfil] = useState<Perfil>("Recepcionista");
  const [telefone, setTelefone] = useState("");
  const [cpf, setCpf] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await chamarApi("/painel/usuarios", {
        metodo: "POST",
        corpo: { nome, email, senha, perfil, telefone: telefone || null, cpf: cpf || null },
      });
      setNome("");
      setEmail("");
      setSenha("");
      setTelefone("");
      setCpf("");
      await aoCriar();
    } catch {
      setErro("Não foi possível criar o usuário. Confira se o e-mail já não está cadastrado.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo="Novo usuário" aberto={aberto} aoFechar={aoFechar}>
      <form onSubmit={aoEnviar} className="space-y-3">
        <label>
          <span className={classeLabel}>Nome</span>
          <input required className={classeInput} value={nome} onChange={(e) => setNome(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>E-mail</span>
          <input required type="email" className={classeInput} value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Senha</span>
          <input required type="password" minLength={8} className={classeInput} value={senha} onChange={(e) => setSenha(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Perfil</span>
          <select className={classeInput} value={perfil} onChange={(e) => setPerfil(e.target.value as Perfil)}>
            {PERFIS.map((p) => (
              <option key={p.valor} value={p.valor}>
                {p.rotulo}
              </option>
            ))}
          </select>
        </label>
        <label>
          <span className={classeLabel}>Telefone (opcional)</span>
          <input className={classeInput} value={telefone} onChange={(e) => setTelefone(e.target.value)} placeholder="+5571988887777" />
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
