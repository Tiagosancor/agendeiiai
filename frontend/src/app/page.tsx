import { headers } from "next/headers";
import type { Metadata } from "next";
import { extrairSlugDoHost } from "@/lib/dominio";
import { buscarNegocioPorSlug, buscarPlanos } from "@/lib/api-servidor";
import { PaginaNegocio } from "@/components/publico/PaginaNegocio";
import { SiteProduto } from "@/components/site/SiteProduto";

/**
 * Serve dois papéis, dependendo do host resolvido pelo `proxy.ts` (seção 8.3.3):
 * - `{slug}.{dominio}`: a página pública do negócio (seção 6.1) — o `proxy.ts` já
 *   confirmou que o negócio existe antes de deixar a requisição chegar aqui.
 * - domínio base ou `app.{dominio}`: sem negócio, o site do produto (seção 6.4).
 *
 * `force-dynamic` é obrigatório aqui: `MARCA_DOMINIO` só existe como variável de
 * ambiente em runtime (nunca em build-time, dentro do Dockerfile), então na primeira
 * renderização estática (no build da imagem) ela vem vazia e este componente cairia
 * sempre no branch "sem negócio" — Next.js então cacheia esse resultado como página
 * estática e serve o mesmo HTML pra qualquer subdomínio depois, ignorando o Host de
 * verdade de cada requisição. Sem isso, `acme.{dominio}` e `beta.{dominio}` mostrariam
 * a mesma página (a primeira que "ganhou" o cache no build).
 */
export const dynamic = "force-dynamic";
export async function generateMetadata(): Promise<Metadata> {
  const negocio = await obterNegocioDoHostAtual();
  if (negocio) return { title: negocio.nomeExibido };

  // Site do produto (seção 6.4): SEO e imagem de compartilhamento só aqui — a página de
  // cada negócio nunca leva a marca do produto (white-label, seção 5).
  const nomeProduto = process.env.MARCA_NOME_PRODUTO ?? "Plataforma";
  const titulo = `${nomeProduto}: agendamento online para barbearias, salões e clínicas`;
  const descricao =
    "Seus clientes marcam sozinhos pelo link do seu negócio e confirmam com um código. Agenda por profissional, lembretes, financeiro e fidelidade. Teste grátis por 30 dias, sem cartão.";
  const listaCabecalhos = await headers();
  const host = listaCabecalhos.get("x-forwarded-host") ?? listaCabecalhos.get("host") ?? "localhost";
  const esquema = listaCabecalhos.get("x-forwarded-proto") ?? (host.includes("localhost") ? "http" : "https");

  return {
    metadataBase: new URL(`${esquema}://${host}`),
    title: { absolute: titulo },
    description: descricao,
    alternates: { canonical: "/" },
    openGraph: {
      type: "website",
      locale: "pt_BR",
      siteName: nomeProduto,
      title: titulo,
      description: descricao,
      url: "/",
      images: [{ url: "/site/og.png", width: 1200, height: 630, alt: nomeProduto }],
    },
    twitter: { card: "summary_large_image", title: titulo, description: descricao, images: ["/site/og.png"] },
  };
}

export default async function Home() {
  const negocio = await obterNegocioDoHostAtual();

  if (negocio) {
    return <PaginaNegocio negocio={negocio} />;
  }

  const planos = await buscarPlanos();
  return (
    <SiteProduto
      nomeProduto={process.env.MARCA_NOME_PRODUTO ?? "Plataforma"}
      planos={planos}
      whatsApp={(process.env.CONTATO_WHATSAPP ?? "").replace(/D/g, "") || null}
    />
  );
}

async function obterNegocioDoHostAtual() {
  const dominioBase = process.env.MARCA_DOMINIO ?? "";
  if (!dominioBase) return null;

  const listaCabecalhos = await headers();
  const host = listaCabecalhos.get("host") ?? "";
  const slug = extrairSlugDoHost(host, dominioBase);

  return slug ? buscarNegocioPorSlug(slug) : null;
}
