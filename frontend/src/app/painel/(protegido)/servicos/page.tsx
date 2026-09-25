"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import type { CategoriaResumo, ServicoResumo } from "@/lib/tipos";
import { formatarReais } from "@/lib/formatacao";

export default function PaginaServicos() {
  const { chamarApi } = useAutenticacao();
  const [categorias, setCategorias] = useState<CategoriaResumo[] | null>(null);
  const [servicos, setServicos] = useState<ServicoResumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [modalCategoriaAberto, setModalCategoriaAberto] = useState(false);
  const [modalServicoAberto, setModalServicoAberto] = useState(false);

  const carregar = useCallback(async () => {
    try {
      const [listaCategorias, listaServicos] = await Promise.all([
        chamarApi<CategoriaResumo[]>("/painel/categorias"),
        chamarApi<ServicoResumo[]>("/painel/servicos"),
      ]);
      setCategorias(listaCategorias);
      setServicos(listaServicos);
    } catch {
      setErro("Não foi possível carregar categorias e serviços.");
    }
  }, [chamarApi]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- busca disparada pela montagem.
    carregar();
  }, [carregar]);

  async function alternarCategoria(categoria: CategoriaResumo) {
    const acao = categoria.ativa ? "desativar" : "ativar";
    await chamarApi(`/painel/categorias/${categoria.id}/${acao}`, { metodo: "POST" });
    await carregar();
  }

  async function alternarServico(servico: ServicoResumo) {
    const acao = servico.ativo ? "desativar" : "ativar";
    await chamarApi(`/painel/servicos/${servico.id}/${acao}`, { metodo: "POST" });
    await carregar();
  }

  const nomeCategoria = (categoriaId: string) => categorias?.find((c) => c.id === categoriaId)?.nome ?? "—";

  return (
    <div className="space-y-8">
      {erro && <p className="text-sm text-red-600">{erro}</p>}

      <section>
        <div className="mb-3 flex items-center justify-between">
          <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Categorias</h1>
          <button className={classeBotaoPrimario} onClick={() => setModalCategoriaAberto(true)}>
            Nova categoria
          </button>
        </div>
        <div className="flex flex-wrap gap-2">
          {categorias?.map((categoria) => (
            <button
              key={categoria.id}
              onClick={() => alternarCategoria(categoria)}
              title={categoria.ativa ? "Clique para desativar" : "Clique para ativar"}
              className={`rounded-full border px-3 py-1 text-sm ${
                categoria.ativa
                  ? "border-blue-300 bg-blue-50 text-blue-700 dark:border-blue-900 dark:bg-blue-950 dark:text-blue-300"
                  : "border-gray-300 bg-gray-100 text-gray-500 dark:border-neutral-700 dark:bg-neutral-800 dark:text-neutral-400"
              }`}
            >
              {categoria.nome}
            </button>
          ))}
          {categorias?.length === 0 && <p className="text-sm text-gray-500 dark:text-neutral-400">Nenhuma categoria ainda.</p>}
        </div>
      </section>

      <section>
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Serviços</h2>
          <button
            className={classeBotaoPrimario}
            disabled={!categorias?.length}
            onClick={() => setModalServicoAberto(true)}
          >
            Novo serviço
          </button>
        </div>

        <div className={classeCartao}>
          <div className="overflow-x-auto">
            <table className="w-full min-w-162.5">
              <thead className="border-b border-gray-200 dark:border-neutral-800">
                <tr>
                  <th className={classeTh}>Nome</th>
                  <th className={classeTh}>Categoria</th>
                  <th className={classeTh}>Preço</th>
                  <th className={classeTh}>Duração</th>
                  <th className={classeTh}>Popular</th>
                  <th className={classeTh}>Status</th>
                  <th className={classeTh}>Ações</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-neutral-800">
                {servicos?.map((servico) => (
                  <tr key={servico.id}>
                    <td className={classeTd}>{servico.nome}</td>
                    <td className={classeTd}>{nomeCategoria(servico.categoriaId)}</td>
                    <td className={classeTd}>{formatarReais(servico.preco)}</td>
                    <td className={classeTd}>{servico.duracaoMinutos} min</td>
                    <td className={classeTd}>{servico.popular ? "Sim" : "Não"}</td>
                    <td className={classeTd}>{servico.ativo ? "Ativo" : "Inativo"}</td>
                    <td className={classeTd}>
                      <button className="text-gray-600 hover:underline dark:text-neutral-300" onClick={() => alternarServico(servico)}>
                        {servico.ativo ? "Desativar" : "Ativar"}
                      </button>
                    </td>
                  </tr>
                ))}
                {servicos?.length === 0 && (
                  <tr>
                    <td className={classeTd} colSpan={7}>
                      Nenhum serviço cadastrado ainda.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      </section>

      <ModalNovaCategoria
        aberto={modalCategoriaAberto}
        aoFechar={() => setModalCategoriaAberto(false)}
        aoCriar={async () => {
          setModalCategoriaAberto(false);
          await carregar();
        }}
      />

      <ModalNovoServico
        categorias={categorias ?? []}
        aberto={modalServicoAberto}
        aoFechar={() => setModalServicoAberto(false)}
        aoCriar={async () => {
          setModalServicoAberto(false);
          await carregar();
        }}
      />
    </div>
  );
}

function ModalNovaCategoria({ aberto, aoFechar, aoCriar }: { aberto: boolean; aoFechar: () => void; aoCriar: () => Promise<void> }) {
  const { chamarApi } = useAutenticacao();
  const [nome, setNome] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await chamarApi("/painel/categorias", { metodo: "POST", corpo: { nome } });
      setNome("");
      await aoCriar();
    } catch {
      setErro("Não foi possível criar a categoria.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo="Nova categoria" aberto={aberto} aoFechar={aoFechar}>
      <form onSubmit={aoEnviar} className="space-y-3">
        <label>
          <span className={classeLabel}>Nome</span>
          <input required className={classeInput} value={nome} onChange={(e) => setNome(e.target.value)} />
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

function ModalNovoServico({
  categorias,
  aberto,
  aoFechar,
  aoCriar,
}: {
  categorias: CategoriaResumo[];
  aberto: boolean;
  aoFechar: () => void;
  aoCriar: () => Promise<void>;
}) {
  const { chamarApi } = useAutenticacao();
  const [categoriaId, setCategoriaId] = useState(categorias[0]?.id ?? "");
  const [nome, setNome] = useState("");
  const [preco, setPreco] = useState("");
  const [duracao, setDuracao] = useState("30");
  const [popular, setPopular] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    // categorias chega depois do primeiro render (busca assíncrona no componente pai);
    // sincroniza o valor padrão do select quando isso acontece.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (!categoriaId && categorias[0]) setCategoriaId(categorias[0].id);
  }, [categorias, categoriaId]);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await chamarApi("/painel/servicos", {
        metodo: "POST",
        corpo: { categoriaId, nome, preco: Number(preco), duracaoMinutos: Number(duracao), popular },
      });
      setNome("");
      setPreco("");
      setDuracao("30");
      setPopular(false);
      await aoCriar();
    } catch {
      setErro("Não foi possível criar o serviço.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo="Novo serviço" aberto={aberto} aoFechar={aoFechar}>
      <form onSubmit={aoEnviar} className="space-y-3">
        <label>
          <span className={classeLabel}>Categoria</span>
          <select required className={classeInput} value={categoriaId} onChange={(e) => setCategoriaId(e.target.value)}>
            {categorias.map((c) => (
              <option key={c.id} value={c.id}>
                {c.nome}
              </option>
            ))}
          </select>
        </label>
        <label>
          <span className={classeLabel}>Nome</span>
          <input required className={classeInput} value={nome} onChange={(e) => setNome(e.target.value)} />
        </label>
        <div className="flex gap-3">
          <label className="flex-1">
            <span className={classeLabel}>Preço (R$)</span>
            <input required type="number" min={0} step="0.01" className={classeInput} value={preco} onChange={(e) => setPreco(e.target.value)} />
          </label>
          <label className="flex-1">
            <span className={classeLabel}>Duração (min)</span>
            <input required type="number" min={1} className={classeInput} value={duracao} onChange={(e) => setDuracao(e.target.value)} />
          </label>
        </div>
        <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-neutral-200">
          <input type="checkbox" checked={popular} onChange={(e) => setPopular(e.target.checked)} />
          Serviço popular (aparece em destaque)
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
