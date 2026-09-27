"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { ErroApi } from "@/lib/api";
import { Modal } from "@/components/Modal";
import { ModalExclusao } from "@/components/painel/ModalExclusao";
import { CamposEndereco } from "@/components/painel/CamposEndereco";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import { ENDERECO_VAZIO, PERFIS, PERMISSOES, type Endereco, type Perfil, type Permissao, type UsuarioDetalhe, type UsuarioResumo } from "@/lib/tipos";

export default function PaginaUsuarios() {
  const { chamarApi, temPermissao } = useAutenticacao();
  const podeEditar = temPermissao("EditarCadastros");
  const podeExcluir = temPermissao("ExcluirCadastros");

  const [usuarios, setUsuarios] = useState<UsuarioResumo[] | null>(null);
  const [usuarioEditando, setUsuarioEditando] = useState<UsuarioDetalhe | null>(null);
  const [idExcluindo, setIdExcluindo] = useState<string | null>(null);
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
    setErro(null);
    try {
      await chamarApi(`/painel/usuarios/${usuario.id}/${acao}`, { metodo: "POST" });
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível alterar o usuário.");
    }
    await carregar();
  }

  async function abrirPermissoes(id: string) {
    const detalhe = await chamarApi<UsuarioDetalhe>(`/painel/usuarios/${id}`);
    setUsuarioPermissoes(detalhe);
  }

  async function alternarPermissao(permissao: Permissao, concedida: boolean) {
    if (!usuarioPermissoes) return;
    const metodo = concedida ? "DELETE" : "POST";
    try {
      await chamarApi(`/painel/usuarios/${usuarioPermissoes.id}/permissoes/${permissao}`, { metodo });
    } catch (excecao) {
      // Ex.: tirar a gestão de usuários do último Administrador (seção 7).
      alert(excecao instanceof ErroApi ? excecao.message : "Não foi possível alterar a permissão.");
    }
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
                    {podeEditar && (
                      <button
                        className="text-marca-primaria hover:underline dark:text-marca-acento"
                        onClick={async () => setUsuarioEditando(await chamarApi<UsuarioDetalhe>(`/painel/usuarios/${usuario.id}`))}
                      >
                        Editar
                      </button>
                    )}
                    <button className="text-gray-600 hover:underline dark:text-neutral-300" onClick={() => alternarAtivo(usuario)}>
                      {usuario.ativo ? "Desativar" : "Ativar"}
                    </button>
                    {podeExcluir && (
                      <button className="text-red-600 hover:underline dark:text-red-400" onClick={() => setIdExcluindo(usuario.id)}>
                        Excluir
                      </button>
                    )}
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

      {usuarioEditando && (
        <ModalEditarUsuario
          usuario={usuarioEditando}
          aoFechar={() => setUsuarioEditando(null)}
          aoSalvar={async () => {
            setUsuarioEditando(null);
            await carregar();
          }}
        />
      )}

      <ModalExclusao
        tipo="usuario"
        id={idExcluindo}
        aoFechar={() => setIdExcluindo(null)}
        aoExcluir={async () => {
          setIdExcluindo(null);
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
          <input className={classeInput} value={telefone} onChange={(e) => setTelefone(e.target.value)} type="tel" placeholder="(71) 98888-7777" />
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

/** Correção da ficha (seção 7). O CPF só é trocado se um novo for digitado — o atual nunca volta completo para a tela. */
function ModalEditarUsuario({
  usuario,
  aoFechar,
  aoSalvar,
}: {
  usuario: UsuarioDetalhe;
  aoFechar: () => void;
  aoSalvar: () => Promise<void>;
}) {
  const { chamarApi } = useAutenticacao();
  const [nome, setNome] = useState(usuario.nome);
  const [email, setEmail] = useState(usuario.email);
  const [telefone, setTelefone] = useState(usuario.telefone ?? "");
  const [cpf, setCpf] = useState("");
  const [fotoUrl, setFotoUrl] = useState(usuario.fotoUrl ?? "");
  const [endereco, setEndereco] = useState<Endereco>(usuario.endereco ?? ENDERECO_VAZIO);
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await chamarApi(`/painel/usuarios/${usuario.id}`, {
        metodo: "PUT",
        corpo: { nome, email, telefone: telefone || null, cpf: cpf || null, endereco, fotoUrl },
      });
      await aoSalvar();
    } catch (excecao) {
      setErro(excecao instanceof ErroApi && excecao.status === 409 ? "Este e-mail já é de outro usuário." : "Não foi possível salvar. Confira os dados.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo={`Editar ${usuario.nome}`} aberto aoFechar={aoFechar}>
      <form onSubmit={aoEnviar} className="space-y-3">
        <label>
          <span className={classeLabel}>Nome</span>
          <input required className={classeInput} value={nome} onChange={(e) => setNome(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>E-mail (usado no login)</span>
          <input required type="email" className={classeInput} value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Telefone</span>
          <input className={classeInput} value={telefone} onChange={(e) => setTelefone(e.target.value)} type="tel" placeholder="(71) 98888-7777" />
        </label>
        <label>
          <span className={classeLabel}>CPF {usuario.cpfMascarado ? `(atual: ${usuario.cpfMascarado})` : ""}</span>
          <input className={classeInput} value={cpf} onChange={(e) => setCpf(e.target.value)} placeholder="Deixe em branco para manter" />
        </label>
        <label>
          <span className={classeLabel}>Foto (endereço da imagem)</span>
          <input type="url" className={classeInput} value={fotoUrl} onChange={(e) => setFotoUrl(e.target.value)} placeholder="https://..." />
        </label>
        <CamposEndereco valor={endereco} aoMudar={setEndereco} />

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
