"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { ErroApi } from "@/lib/api";
import { classeBotaoPrimario, classeBotaoSecundario, classeInput, classeLabel } from "@/components/estilos";

interface SituacaoLink {
  slug: string;
  url: string;
  ultimaTrocaEm: string | null;
  /** Nulo = pode trocar agora. */
  proximaTrocaEm: string | null;
  podeEditar: boolean;
}

interface Disponibilidade {
  disponivel: boolean;
  motivo: string | null;
}

function semEsquema(url: string): string {
  return url.replace(/^https?:\/\//, "").replace(/\/$/, "");
}

function dataCurta(iso: string): string {
  return new Date(iso).toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit", year: "numeric" });
}

/**
 * Link da página do negócio em "Meu negócio" (seção 5, "Editar o link depois do cadastro"): mostra o endereço atual e, para o
 * Administrador, "Editar" com checagem em tempo real (as regras são revalidadas pela API ao salvar), o aviso de que o endereço
 * muda para fora e a confirmação explícita. O antigo redireciona por 90 dias; uma troca a cada 30 dias.
 */
export function CampoLinkNegocio() {
  const { chamarApi } = useAutenticacao();
  const [situacao, setSituacao] = useState<SituacaoLink | null>(null);
  const [editando, setEditando] = useState(false);
  const [novo, setNovo] = useState("");
  const [disponibilidade, setDisponibilidade] = useState<Disponibilidade | null>(null);
  const [entendi, setEntendi] = useState(false);
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [sucesso, setSucesso] = useState<string | null>(null);
  const consultaAtual = useRef(0);

  const carregar = useCallback(async () => {
    try {
      setSituacao(await chamarApi<SituacaoLink>("/painel/negocio/link"));
    } catch {
      setErro("Não foi possível carregar o link da página.");
    }
  }, [chamarApi]);

  useEffect(() => {
    // Busca disparada pela montagem.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  // Checagem em tempo real, com espera curta entre as teclas; resposta de uma digitação anterior é descartada.
  useEffect(() => {
    if (!editando) return;
    const valor = novo.trim().toLowerCase();
    const numero = ++consultaAtual.current;
    if (!valor) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setDisponibilidade(null);
      return;
    }
    const espera = setTimeout(async () => {
      try {
        const resposta = await chamarApi<Disponibilidade>(`/painel/negocio/link/disponibilidade?slug=${encodeURIComponent(valor)}`);
        if (numero === consultaAtual.current) setDisponibilidade(resposta);
      } catch {
        if (numero === consultaAtual.current) setDisponibilidade(null);
      }
    }, 350);
    return () => clearTimeout(espera);
  }, [novo, editando, chamarApi]);

  function abrirEdicao() {
    setNovo(situacao?.slug ?? "");
    setDisponibilidade(null);
    setEntendi(false);
    setErro(null);
    setSucesso(null);
    setEditando(true);
  }

  async function salvar() {
    if (!situacao) return;
    setErro(null);
    setSalvando(true);
    const antigo = semEsquema(situacao.url);
    try {
      const nova = await chamarApi<SituacaoLink>("/painel/negocio/link", { metodo: "PUT", corpo: { slug: novo.trim().toLowerCase() } });
      setSituacao(nova);
      setEditando(false);
      setSucesso(`Link trocado. Quem acessar ${antigo} nos próximos 90 dias será levado para o endereço novo.`);
    } catch (e) {
      setErro(e instanceof ErroApi && (e.status === 400 || e.status === 409) ? e.message : "Não foi possível trocar o link.");
    } finally {
      setSalvando(false);
    }
  }

  if (!situacao) return erro ? <p className="text-sm text-red-600">{erro}</p> : null;

  const endereco = semEsquema(situacao.url);
  const sufixo = endereco.slice(situacao.slug.length); // ".agendeiiai.com.br" (com a porta, em dev)
  const podeSalvar = disponibilidade?.disponivel === true && entendi && !salvando;

  return (
    <section className="space-y-3" data-testid="link-negocio">
      <h2 className="text-sm font-semibold tracking-wide text-gray-500 uppercase dark:text-neutral-400">Link da página</h2>

      {!editando ? (
        <div className="flex flex-wrap items-center gap-3">
          <a href={situacao.url} target="_blank" rel="noreferrer" className="font-medium text-gray-900 underline dark:text-neutral-50" data-testid="link-atual">
            {endereco}
          </a>
          {situacao.podeEditar && !situacao.proximaTrocaEm && (
            <button type="button" onClick={abrirEdicao} className={classeBotaoSecundario}>
              Editar
            </button>
          )}
        </div>
      ) : (
        <div className="space-y-3 rounded-lg border border-gray-200 p-4 dark:border-neutral-800">
          <div>
            <label htmlFor="novo-link" className={classeLabel}>
              Novo endereço
            </label>
            <div className="flex items-center gap-1">
              <input
                id="novo-link"
                className={`${classeInput} max-w-60`}
                value={novo}
                autoComplete="off"
                maxLength={30}
                onChange={(e) => setNovo(e.target.value.toLowerCase())}
              />
              <span className="text-sm text-gray-500 dark:text-neutral-400">{sufixo}</span>
            </div>
            {disponibilidade && (
              <p role="status" className={`mt-1 text-sm ${disponibilidade.disponivel ? "text-green-600" : "text-red-600"}`}>
                {disponibilidade.disponivel ? "Disponível" : disponibilidade.motivo}
              </p>
            )}
          </div>

          <div className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100">
            <p className="font-semibold">Isso muda o endereço que seus clientes já têm.</p>
            <p className="mt-1">
              {endereco} pode estar em cartões de visita, QR codes, conversas de WhatsApp e posts salvos. Por 90 dias, quem acessar o
              link antigo será levado ao novo; depois disso, o antigo deixa de funcionar. Você só poderá trocar de novo daqui a 30 dias.
            </p>
            <label className="mt-2 flex items-center gap-2">
              <input type="checkbox" checked={entendi} onChange={(e) => setEntendi(e.target.checked)} />
              Entendi, quero trocar o link
            </label>
          </div>

          {erro && <p className="text-sm text-red-600">{erro}</p>}

          <div className="flex gap-2">
            <button type="button" onClick={salvar} disabled={!podeSalvar} className={classeBotaoPrimario}>
              {salvando ? "Trocando..." : "Trocar link"}
            </button>
            <button type="button" onClick={() => setEditando(false)} className={classeBotaoSecundario}>
              Cancelar
            </button>
          </div>
        </div>
      )}

      {situacao.podeEditar && situacao.proximaTrocaEm && !editando && (
        <p className="text-sm text-gray-500 dark:text-neutral-400">
          O link foi trocado recentemente. Você poderá trocar de novo em {dataCurta(situacao.proximaTrocaEm)}.
        </p>
      )}
      {!situacao.podeEditar && <p className="text-sm text-gray-500 dark:text-neutral-400">Só o Administrador pode trocar o link.</p>}
      {sucesso && <p className="text-sm text-green-600">{sucesso}</p>}
    </section>
  );
}
