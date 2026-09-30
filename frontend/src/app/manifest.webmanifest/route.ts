import { headers } from "next/headers";
import { extrairSlugDoHost } from "@/lib/dominio";
import { buscarNegocioPorSlug } from "@/lib/api-servidor";

/**
 * Os dois PWAs (seção 5, "Dois PWAs instaláveis separados"), escolhidos pelo host da requisição — a mesma regra de slug do
 * `proxy.ts`:
 * - `{slug}.{dominio}`: o app **do negócio** — nome, cores e ícone dele (`/icone/*`), abre na página dele. Nunca a marca do
 *   produto: se o negócio não puder ser lido agora, responde 503 em vez de cair no manifesto do painel.
 * - qualquer outro host (`app.{dominio}`, domínio raiz, dev sem subdomínio): o **painel**, com a marca do produto, abrindo
 *   em `/painel/login` (que segue para a última tela se a sessão ainda vale).
 *
 * Route handler (e não `app/manifest.ts`) para controlar o cache: `no-cache` + `Vary: Host`, nenhum CDN guarda o manifesto
 * de um host e entrega a outro. Dinâmico porque `MARCA_DOMINIO` só existe em runtime (ver CLAUDE.md).
 */
export const dynamic = "force-dynamic";

const CABECALHOS = {
  "Content-Type": "application/manifest+json; charset=utf-8",
  "Cache-Control": "no-cache, private",
  Vary: "Host",
};

export async function GET() {
  const dominioBase = process.env.MARCA_DOMINIO ?? "";
  const listaCabecalhos = await headers();
  const slug = dominioBase ? extrairSlugDoHost(listaCabecalhos.get("host") ?? "", dominioBase) : null;

  if (slug) {
    const negocio = await buscarNegocioPorSlug(slug).catch(() => null);
    if (!negocio) return new Response(null, { status: 503, headers: { "Cache-Control": "no-store", Vary: "Host" } });

    const manifesto = {
      id: "/",
      name: negocio.nomeExibido,
      short_name: negocio.nomeExibido,
      description: `Agende seu horário — ${negocio.nomeExibido}`,
      lang: "pt-BR",
      start_url: "/",
      scope: "/",
      display: "standalone",
      background_color: negocio.corFundo ?? "#ffffff",
      theme_color: negocio.corPrimaria ?? negocio.corFundo ?? "#2563eb",
      icons: [
        { src: "/icone/192", sizes: "192x192", type: "image/png" },
        { src: "/icone/512", sizes: "512x512", type: "image/png" },
        { src: "/icone/maskable-192", sizes: "192x192", type: "image/png", purpose: "maskable" },
        { src: "/icone/maskable-512", sizes: "512x512", type: "image/png", purpose: "maskable" },
      ],
    };
    return Response.json(manifesto, { headers: CABECALHOS });
  }

  const nomeProduto = process.env.MARCA_NOME_PRODUTO || "Painel";
  const manifesto = {
    id: "/painel/",
    name: nomeProduto,
    short_name: nomeProduto,
    description: `Painel do seu negócio — ${nomeProduto}`,
    lang: "pt-BR",
    start_url: "/painel/login",
    scope: "/",
    display: "standalone",
    // Paleta do produto (seção 5.1): neutro claro de fundo, azul-marinho na barra.
    background_color: "#FAF9F6",
    theme_color: "#1E2A38",
    icons: [
      { src: "/brand/icone-192.png", sizes: "192x192", type: "image/png" },
      { src: "/brand/icone-512.png", sizes: "512x512", type: "image/png" },
      { src: "/brand/maskable-192.png", sizes: "192x192", type: "image/png", purpose: "maskable" },
      { src: "/brand/maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  };
  return Response.json(manifesto, { headers: CABECALHOS });
}
