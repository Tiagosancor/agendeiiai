import type { NegocioPublico, PlanoPublico } from "@/lib/tipos";

// Usada só por Server Components (ex.: a página pública buscando o negócio antes de
// renderizar) — API_URL_INTERNA é o nome do serviço Docker (http://api:8080), alcançável
// de dentro do container do Next.js; diferente de NEXT_PUBLIC_API_URL, que é o endereço
// que o NAVEGADOR usa (ver docs/decisoes.md e proxy.ts).
const urlApiInterna = process.env.API_URL_INTERNA ?? "http://localhost:5080";

export async function buscarNegocioPorSlug(slug: string): Promise<NegocioPublico | null> {
  const resposta = await fetch(`${urlApiInterna}/publico/negocios-por-slug/${slug}`, {
    headers: { accept: "application/json" },
    cache: "no-store",
  });

  if (!resposta.ok) return null;
  return (await resposta.json()) as NegocioPublico;
}

/** Planos ativos para o site do produto (seção 6.4) — lidos do banco, nunca fixos no front. */
export async function buscarPlanos(): Promise<PlanoPublico[]> {
  try {
    const resposta = await fetch(`${urlApiInterna}/cadastro/planos`, {
      headers: { accept: "application/json" },
      cache: "no-store",
    });
    return resposta.ok ? ((await resposta.json()) as PlanoPublico[]) : [];
  } catch {
    // API fora do ar: o site continua de pé, só sem os cards (a seção mostra o aviso).
    return [];
  }
}
