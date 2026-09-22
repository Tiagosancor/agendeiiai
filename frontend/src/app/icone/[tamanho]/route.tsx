import { ImageResponse } from "next/og";
import { headers } from "next/headers";
import { extrairSlugDoHost } from "@/lib/dominio";
import { buscarNegocioPorSlug } from "@/lib/api-servidor";

/**
 * Ícone da PWA gerado na hora (Sprint 5) — evita precisar de arquivo de imagem estático
 * (que não teria como refletir a cor/nome de CADA negócio, seção 5) ou de uma etapa de
 * design fora deste repositório. Só as iniciais do negócio sobre a cor primária dele.
 */
export const dynamic = "force-dynamic";

function iniciais(nome: string): string {
  return nome
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((parte) => parte[0]?.toUpperCase())
    .join("");
}

export async function GET(request: Request, { params }: { params: Promise<{ tamanho: string }> }) {
  const { tamanho } = await params;
  const tamanhoNumerico = tamanho === "512" ? 512 : 192;

  const dominioBase = process.env.MARCA_DOMINIO ?? "";
  const nomeProduto = process.env.MARCA_NOME_PRODUTO || "Painel";

  const listaCabecalhos = await headers();
  const slug = dominioBase ? extrairSlugDoHost(listaCabecalhos.get("host") ?? "", dominioBase) : null;
  const negocio = slug ? await buscarNegocioPorSlug(slug) : null;

  const nome = negocio?.nomeExibido ?? nomeProduto;
  const cor = negocio?.corPrimaria ?? "#2563eb";

  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: cor,
          color: "#ffffff",
          fontSize: tamanhoNumerico * 0.45,
          fontWeight: 700,
          fontFamily: "sans-serif",
        }}
      >
        {iniciais(nome) || "A"}
      </div>
    ),
    { width: tamanhoNumerico, height: tamanhoNumerico },
  );
}
