import Link from "next/link";
import { headers } from "next/headers";
import type { Metadata } from "next";
import { extrairSlugDoHost } from "@/lib/dominio";
import { buscarNegocioPorSlug } from "@/lib/api-servidor";
import { PaginaNegocio } from "@/components/publico/PaginaNegocio";

/**
 * Serve dois papéis, dependendo do host resolvido pelo `proxy.ts` (seção 8.3.3):
 * - `{slug}.{dominio}`: a página pública do negócio (seção 6.1) — o `proxy.ts` já
 *   confirmou que o negócio existe antes de deixar a requisição chegar aqui.
 * - domínio base ou `app.{dominio}`: sem negócio, só um link para o painel.
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
  return negocio ? { title: negocio.nomeExibido } : {};
}

export default async function Home() {
  const negocio = await obterNegocioDoHostAtual();

  if (negocio) {
    return <PaginaNegocio negocio={negocio} />;
  }

  const nomeProduto = process.env.MARCA_NOME_PRODUTO ?? "Plataforma";

  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 px-6 text-center">
      {/* Marca completa (seção 5.1) — único contexto "grande" que a marca do produto em si
          tem hoje (sem tenant resolvido); não existe landing/site de vendas neste MVP. */}
      <img src="/brand/agendeiiai-marca-completa.svg" alt="" width={96} height={96} />
      <h1 className="font-display text-2xl font-bold text-gray-900 dark:text-neutral-50">{nomeProduto}</h1>
      <p className="max-w-sm text-sm text-gray-500 dark:text-neutral-400">
        Acesse pelo endereço do seu negócio para ver a página de agendamento, ou entre no painel.
      </p>
      <Link
        href="/painel/login"
        className="rounded-lg bg-marca-primaria px-4 py-2 text-sm font-medium text-white transition hover:bg-marca-primaria-hover"
      >
        Entrar no painel
      </Link>
    </main>
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
