import type { MetadataRoute } from "next";
import { headers } from "next/headers";
import { extrairSlugDoHost } from "@/lib/dominio";
import { buscarNegocioPorSlug } from "@/lib/api-servidor";

/**
 * Manifest da PWA (Sprint 5) — dinâmico por subdomínio: cada negócio instala como o
 * PRÓPRIO app (nome e cor da marca do negócio, nunca o nome do produto — seção 5), não um
 * app genérico "Agendei". Mesma armadilha do `app/page.tsx` (ver CLAUDE.md): `MARCA_DOMINIO`
 * só existe em runtime, então isso precisa ser dinâmico, nunca prerenderizado no build.
 */
export const dynamic = "force-dynamic";

export default async function manifest(): Promise<MetadataRoute.Manifest> {
  const dominioBase = process.env.MARCA_DOMINIO ?? "";
  const nomeProduto = process.env.MARCA_NOME_PRODUTO || "Painel";

  const listaCabecalhos = await headers();
  const slug = dominioBase ? extrairSlugDoHost(listaCabecalhos.get("host") ?? "", dominioBase) : null;
  const negocio = slug ? await buscarNegocioPorSlug(slug) : null;

  const nome = negocio?.nomeExibido ?? nomeProduto;
  const cor = negocio?.corPrimaria ?? "#2563eb";

  return {
    name: nome,
    short_name: nome,
    description: `Agendamento online — ${nome}`,
    start_url: "/",
    display: "standalone",
    background_color: "#ffffff",
    theme_color: cor,
    icons: [
      { src: "/icone/192", sizes: "192x192", type: "image/png" },
      { src: "/icone/512", sizes: "512x512", type: "image/png" },
    ],
  };
}
