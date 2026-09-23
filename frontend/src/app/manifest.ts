import type { MetadataRoute } from "next";
import { headers } from "next/headers";
import { extrairSlugDoHost } from "@/lib/dominio";
import { buscarNegocioPorSlug } from "@/lib/api-servidor";

/**
 * Manifest da PWA (Sprint 5) — dinâmico por subdomínio: cada negócio instala como o
 * PRÓPRIO app (nome e cor da marca do negócio, nunca o nome do produto — seção 5), não um
 * app genérico "Agendeiiai". Mesma armadilha do `app/page.tsx` (ver CLAUDE.md): `MARCA_DOMINIO`
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
  // Sem negócio (painel instalado direto, sem passar por um subdomínio de tenant): usa a
  // cor e os ícones da MARCA DO PRODUTO (seção 5.1), nunca o azul genérico de antes nem os
  // ícones gerados por iniciais (esses continuam só para o PWA de cada negócio, que é
  // livre pra ter sua própria cor — seção 5).
  const cor = negocio?.corPrimaria ?? "#1E2A38";

  const icones: MetadataRoute.Manifest["icons"] = negocio
    ? [
        { src: "/icone/192", sizes: "192x192", type: "image/png" },
        { src: "/icone/512", sizes: "512x512", type: "image/png" },
      ]
    : [
        { src: "/brand/icone-192.png", sizes: "192x192", type: "image/png" },
        { src: "/brand/icone-512.png", sizes: "512x512", type: "image/png" },
        { src: "/brand/maskable-192.png", sizes: "192x192", type: "image/png", purpose: "maskable" },
        { src: "/brand/maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
      ];

  return {
    name: nome,
    short_name: nome,
    description: `Agendamento online — ${nome}`,
    start_url: "/",
    display: "standalone",
    background_color: "#ffffff",
    theme_color: cor,
    icons: icones,
  };
}
