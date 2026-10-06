"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useAutenticacao } from "@/lib/auth-context";
import { CabecalhoCadastro, StatusCadastro, CarregandoCadastro } from "@/components/painel/CadastrosVisuais";
import { Modal } from "@/components/Modal";
import { ModalExclusao } from "@/components/painel/ModalExclusao";
import { CamposEndereco } from "@/components/painel/CamposEndereco";
import { AvisoLimitePlano, ModalCriarProfissional } from "@/components/painel/ModalCriarProfissional";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import { ENDERECO_VAZIO, type Endereco, type ProfissionalDetalhe, type ProfissionalResumo } from "@/lib/tipos";
import { ErroApi } from "@/lib/api";

export default function PaginaProfissionais() {
  const { chamarApi, temPermissao } = useAutenticacao();
  const podeEditar = temPermissao("EditarCadastros");
  const podeExcluir = temPermissao("ExcluirCadastros");
  const [profissionais, setProfissionais] = useState<ProfissionalResumo[] | null>(null);
  const [profissionalEditando, setProfissionalEditando] = useState<ProfissionalDetalhe | null>(null);
  const [idExcluindo, setIdExcluindo] = useState<string | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [limiteAtingido, setLimiteAtingido] = useState<string | null>(null);
  const [modalAberto, setModalAberto] = useState(false);
  const router = useRouter();

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
    setLimiteAtingido(null);
    try {
      await chamarApi(`/painel/profissionais/${profissional.id}/${acao}`, { metodo: "POST" });
    } catch (excecao) {
      if (excecao instanceof ErroApi && excecao.codigo === "limite_profissionais") setLimiteAtingido(excecao.message);
      else setErro("Não foi possível alterar o profissional.");
    }
    await carregar();
  }

  return (
    <div className="painel-cadastro">
      <CabecalhoCadastro titulo="Profissionais" descricao="Gerencie os profissionais, horários e serviços do seu negócio.">
        <button className={classeBotaoPrimario} onClick={() => setModalAberto(true)}>
          Novo profissional
        </button>
      </CabecalhoCadastro>

      {erro && <p role="alert" className="mb-4 text-sm text-red-600">{erro}</p>}
      {limiteAtingido && <AvisoLimitePlano mensagem={limiteAtingido} />}

      {!profissionais && !erro && <CarregandoCadastro />}
      <div className={classeCartao}>
        <div className="overflow-x-auto">
          <table role="table" aria-label="Profissionais cadastrados" className="painel-cadastro-tabela w-full min-w-125">
            <thead role="rowgroup" className="border-b border-gray-200 dark:border-neutral-800">
              <tr role="row">
                <th role="columnheader" scope="col" className={classeTh}>Nome</th>
                <th role="columnheader" scope="col" className={classeTh}>Status</th>
                <th role="columnheader" scope="col" className={classeTh}>Ações</th>
              </tr>
            </thead>
            <tbody role="rowgroup" className="divide-y divide-gray-100 dark:divide-neutral-800">
              {profissionais?.map((profissional) => (
                <tr role="row" key={profissional.id}>
                  <td role="cell" data-label="Nome" className={classeTd}>{profissional.nome}</td>
                  <td role="cell" data-label="Status" className={classeTd}><StatusCadastro ativo={profissional.ativo} /></td>
                  <td role="cell" data-label="Ações" className={`${classeTd} space-x-3`}>
                    <Link href={`/painel/profissionais/${profissional.id}`} className="text-marca-primaria hover:underline dark:text-marca-acento">
                      Horários e serviços
                    </Link>
                    {podeEditar && (
                      <button
                        className="text-marca-primaria hover:underline dark:text-marca-acento"
                        onClick={async () => setProfissionalEditando(await chamarApi<ProfissionalDetalhe>(`/painel/profissionais/${profissional.id}`))}
                      >
                        Editar
                      </button>
                    )}
                    <button className="text-gray-600 hover:underline dark:text-neutral-300" onClick={() => alternarAtivo(profissional)}>
                      {profissional.ativo ? "Desativar" : "Ativar"}
                    </button>
                    {podeExcluir && (
                      <button className="text-red-600 hover:underline dark:text-red-400" onClick={() => setIdExcluindo(profissional.id)}>
                        Excluir
                      </button>
                    )}
                  </td>
                </tr>
              ))}
              {profissionais?.length === 0 && (
                <tr role="row">
                  <td className={classeTd} colSpan={3}>
                    Nenhum profissional cadastrado ainda.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {profissionalEditando && (
        <ModalEditarProfissional
          profissional={profissionalEditando}
          aoFechar={() => setProfissionalEditando(null)}
          aoSalvar={async () => {
            setProfissionalEditando(null);
            await carregar();
          }}
        />
      )}

      <ModalExclusao
        tipo="profissional"
        id={idExcluindo}
        aoFechar={() => setIdExcluindo(null)}
        aoExcluir={async () => {
          setIdExcluindo(null);
          await carregar();
        }}
      />

      <ModalCriarProfissional
        aberto={modalAberto}
        aoFechar={() => setModalAberto(false)}
        aoCriar={async (id) => {
          setModalAberto(false);
          // Horários, serviços e comissão ficam na ficha — segue direto para ela.
          router.push(`/painel/profissionais/${id}`);
        }}
      />
    </div>
  );
}

/**
 * Correção da ficha (seção 7). Serviços executados e preços próprios ficam em "Horários e
 * serviços". O CPF só é trocado se um novo for digitado.
 */
function ModalEditarProfissional({
  profissional,
  aoFechar,
  aoSalvar,
}: {
  profissional: ProfissionalDetalhe;
  aoFechar: () => void;
  aoSalvar: () => Promise<void>;
}) {
  const { chamarApi } = useAutenticacao();
  const [nome, setNome] = useState(profissional.nome);
  const [funcao, setFuncao] = useState(profissional.funcao ?? "");
  const [telefone, setTelefone] = useState(profissional.telefone ?? "");
  const [email, setEmail] = useState(profissional.email ?? "");
  const [cpf, setCpf] = useState("");
  const [fotoUrl, setFotoUrl] = useState(profissional.fotoUrl ?? "");
  const [endereco, setEndereco] = useState<Endereco>(profissional.endereco ?? ENDERECO_VAZIO);
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await chamarApi(`/painel/profissionais/${profissional.id}`, {
        metodo: "PUT",
        corpo: {
          nome,
          funcao: funcao || null,
          telefone: telefone || null,
          email: email || null,
          cpf: cpf || null,
          endereco,
          fotoUrl,
        },
      });
      await aoSalvar();
    } catch {
      setErro("Não foi possível salvar. Confira os dados.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo={`Editar ${profissional.nome}`} aberto aoFechar={aoFechar}>
      <form onSubmit={aoEnviar} className="space-y-3">
        <label>
          <span className={classeLabel}>Nome</span>
          <input required className={classeInput} value={nome} onChange={(e) => setNome(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Função (aparece na página pública)</span>
          <input className={classeInput} value={funcao} onChange={(e) => setFuncao(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Telefone</span>
          <input className={classeInput} value={telefone} onChange={(e) => setTelefone(e.target.value)} type="tel" placeholder="(71) 98888-7777" />
        </label>
        <label>
          <span className={classeLabel}>E-mail (recebe os avisos de agendamento)</span>
          <input type="email" className={classeInput} value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>CPF {profissional.cpfMascarado ? `(atual: ${profissional.cpfMascarado})` : ""}</span>
          <input className={classeInput} value={cpf} onChange={(e) => setCpf(e.target.value)} placeholder="Deixe em branco para manter" />
        </label>
        <label>
          <span className={classeLabel}>Foto (endereço da imagem)</span>
          <input type="url" className={classeInput} value={fotoUrl} onChange={(e) => setFotoUrl(e.target.value)} placeholder="https://..." />
        </label>
        <CamposEndereco valor={endereco} aoMudar={setEndereco} />
        <p className="text-xs text-gray-500 dark:text-neutral-400">
          Serviços que executa e preços próprios: em{" "}
          <Link href={`/painel/profissionais/${profissional.id}`} className="underline">
            Horários e serviços
          </Link>
          .
        </p>

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
