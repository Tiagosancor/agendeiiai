"use client";

import { useState, type ChangeEvent } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { ErroApi } from "@/lib/api";
import { urlArquivo } from "@/lib/fundo";
import { classeBotaoSecundario, classeInput, classeLabel } from "@/components/estilos";

export function CampoFotoProfissional({ rota, fotoInicial, aoMudar }: { rota: string; fotoInicial?: string | null; aoMudar?: (url: string | null) => void }) {
  const { chamarApi } = useAutenticacao();
  const [foto, setFoto] = useState<string | null>(fotoInicial ?? null);
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  async function enviar(evento: ChangeEvent<HTMLInputElement>) {
    const arquivo = evento.target.files?.[0];
    if (!arquivo) return;
    setErro(null);
    setEnviando(true);
    try {
      const formulario = new FormData();
      formulario.append("arquivo", arquivo);
      const resposta = await chamarApi<{ fotoUrl: string }>(rota, { metodo: "PUT", corpo: formulario });
      setFoto(resposta.fotoUrl);
      aoMudar?.(resposta.fotoUrl);
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível enviar a foto.");
    } finally {
      setEnviando(false);
      evento.target.value = "";
    }
  }

  async function remover() {
    setErro(null);
    setEnviando(true);
    try {
      await chamarApi(rota, { metodo: "DELETE" });
      setFoto(null);
      aoMudar?.(null);
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível remover a foto.");
    } finally {
      setEnviando(false);
    }
  }

  return <div className="space-y-2">
    <span className={classeLabel}>Foto do profissional</span>
    <div className="flex flex-wrap items-center gap-3">
      {foto ? <img // eslint-disable-line @next/next/no-img-element
        src={urlArquivo(foto) ?? undefined} alt="Prévia da foto do profissional" className="h-16 w-16 rounded-full border border-gray-200 object-cover dark:border-neutral-700" /> : <span className="flex h-16 w-16 items-center justify-center rounded-full bg-gray-100 text-xs text-gray-500 dark:bg-neutral-800 dark:text-neutral-400">Sem foto</span>}
      <div className="space-y-2">
        <input aria-label="Escolher foto do profissional" type="file" accept="image/jpeg,image/png,image/webp" className={classeInput} disabled={enviando} onChange={enviar} />
        <p className="text-xs text-gray-500 dark:text-neutral-400">JPG, PNG ou WebP, até 5 MB. A imagem será otimizada.</p>
        {foto && <button type="button" className={classeBotaoSecundario} disabled={enviando} onClick={remover}>Remover foto</button>}
      </div>
    </div>
    {erro && <p role="alert" className="text-sm text-red-600">{erro}</p>}
  </div>;
}
