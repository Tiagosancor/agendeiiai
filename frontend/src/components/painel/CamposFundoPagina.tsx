"use client";

import { useRef, useState } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { ErroApi } from "@/lib/api";
import { classeInput, classeLabel } from "@/components/estilos";
import { fundoDoNegocio, urlArquivo } from "@/lib/fundo";

const BYTES_MAXIMOS = 5 * 1024 * 1024;
const TIPOS_ACEITOS = "image/jpeg,image/png,image/webp";

interface Props {
  corFundo: string | null;
  imagemFundoUrl: string | null;
  nomeExibido: string;
  aoMudarCor: (cor: string | null) => void;
  aoMudarImagem: (url: string | null) => void;
}

/**
 * Cor e imagem de fundo da página pública e do assistente (seção 5). A cor vai no "Salvar alterações" do formulário; a
 * imagem é enviada na hora (rota própria, a API valida e reprocessa — seção 8.4) e já vale sem salvar o resto.
 */
export function CamposFundoPagina({ corFundo, imagemFundoUrl, nomeExibido, aoMudarCor, aoMudarImagem }: Props) {
  const { chamarApi } = useAutenticacao();
  const entrada = useRef<HTMLInputElement>(null);
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  const previa = fundoDoNegocio({ corFundo, imagemFundoUrl });

  async function enviar(arquivo: File) {
    setErro(null);
    if (arquivo.size > BYTES_MAXIMOS) {
      setErro("A imagem pode ter até 5 MB.");
      return;
    }

    const formulario = new FormData();
    formulario.append("arquivo", arquivo);
    setEnviando(true);
    try {
      const resposta = await chamarApi<{ imagemFundoUrl: string }>("/painel/negocio/imagem-fundo", { metodo: "PUT", corpo: formulario });
      aoMudarImagem(resposta.imagemFundoUrl);
    } catch (e) {
      setErro(e instanceof ErroApi && e.status === 400 ? e.message : "Não foi possível enviar a imagem.");
    } finally {
      setEnviando(false);
      if (entrada.current) entrada.current.value = "";
    }
  }

  async function remover() {
    if (!confirm("Remover a imagem de fundo? A página passa a usar só a cor.")) return;
    setErro(null);
    try {
      await chamarApi("/painel/negocio/imagem-fundo", { metodo: "DELETE" });
      aoMudarImagem(null);
    } catch {
      setErro("Não foi possível remover a imagem.");
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-end gap-3">
        <div>
          <label htmlFor="cor-fundo" className={classeLabel}>
            Cor de fundo
          </label>
          <div className="flex items-center gap-2">
            <input
              type="color"
              aria-label="Escolher cor de fundo"
              value={corFundo ?? "#1e2a38"}
              onChange={(e) => aoMudarCor(e.target.value)}
              className="h-9 w-12 cursor-pointer rounded border border-gray-300 bg-transparent dark:border-neutral-700"
            />
            <input
              id="cor-fundo"
              className={`${classeInput} w-32`}
              value={corFundo ?? ""}
              placeholder="#1E2A38"
              onChange={(e) => aoMudarCor(e.target.value || null)}
            />
          </div>
        </div>
        {corFundo && (
          <button type="button" onClick={() => aoMudarCor(null)} className="pb-2 text-sm text-gray-500 underline dark:text-neutral-400">
            Sem cor
          </button>
        )}
      </div>

      <div>
        <span className={classeLabel}>Imagem de fundo (opcional)</span>
        <p className="mb-2 text-xs text-gray-500 dark:text-neutral-400">
          JPG, PNG ou WebP, até 5 MB. Sem imagem, a página e o agendamento usam só a cor.
        </p>
        <div className="flex flex-wrap items-center gap-2">
          <label className="cursor-pointer rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-neutral-700">
            {enviando ? "Enviando..." : imagemFundoUrl ? "Trocar imagem" : "Enviar imagem"}
            <input
              ref={entrada}
              type="file"
              accept={TIPOS_ACEITOS}
              className="sr-only"
              disabled={enviando}
              aria-label="Imagem de fundo"
              onChange={(e) => {
                const arquivo = e.target.files?.[0];
                if (arquivo) enviar(arquivo);
              }}
            />
          </label>
          {imagemFundoUrl && (
            <button type="button" onClick={remover} className="text-sm text-red-600 underline">
              Remover imagem
            </button>
          )}
        </div>
        {erro && <p className="mt-2 text-sm text-red-600">{erro}</p>}
      </div>

      <div
        data-testid="previa-fundo"
        className="flex h-28 items-center justify-center rounded-lg border border-gray-200 text-sm font-semibold dark:border-neutral-800"
        style={previa.estilo}
      >
        <span className={previa.temFundo ? (previa.textoClaro ? "text-white" : "text-gray-900") : "text-gray-500 dark:text-neutral-400"}>
          {previa.temFundo ? nomeExibido : "Sem fundo: a página usa o fundo padrão do tema"}
        </span>
      </div>
      {imagemFundoUrl && (
        <a href={urlArquivo(imagemFundoUrl) ?? "#"} target="_blank" rel="noreferrer" className="text-xs text-gray-500 underline">
          Ver imagem enviada
        </a>
      )}
    </div>
  );
}
