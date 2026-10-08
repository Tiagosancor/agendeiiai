import { NextRequest, NextResponse } from "next/server";

/**
 * Resolve o negócio pelo subdomínio do host (seção 8.3.3). Hosts reconhecidos, dado
 * o domínio base configurado em `MARCA_DOMINIO`:
 *   - `{dominio}` ou `app.{dominio}`            → segue normalmente (raiz/painel).
 *   - `{slug}.{dominio}`, slug existe e ativo    → segue normalmente (página do negócio).
 *   - `{slug}.{dominio}`, slug inválido/inexistente → 404.
 *   - host fora do padrão (ex.: localhost puro em dev sem subdomínio) → segue normalmente.
 *
 * A existência do negócio é confirmada na API (`/publico/negocios-por-slug/{slug}`),
 * nunca só pelo formato do slug — um slug com formato válido mas nunca cadastrado
 * também deve dar 404.
 */

const REGEX_SLUG = /^[a-z0-9]([a-z0-9-]*[a-z0-9])?$/;
const TAMANHO_MINIMO_SLUG = 3;
const TAMANHO_MAXIMO_SLUG = 30;

// Sem fallback com nome de produto — se MARCA_DOMINIO não estiver configurada, o
// comportamento seguro é não tentar resolver subdomínio nenhum (ver seção 5: o nome/
// domínio do produto é sempre configuração, nunca um valor fixo no código).
const dominioBase = (process.env.MARCA_DOMINIO ?? "").toLowerCase();
const urlApiInterna = process.env.API_URL_INTERNA ?? "http://localhost:5080";

function formatoDeSlugValido(candidato: string): boolean {
  return (
    candidato.length >= TAMANHO_MINIMO_SLUG &&
    candidato.length <= TAMANHO_MAXIMO_SLUG &&
    REGEX_SLUG.test(candidato)
  );
}

/**
 * O alvo do rewrite chama `notFound()` e pode ser pré-renderizado pelo Next.js. Sem
 * headers explícitos, esse 404 recebe cache público prolongado e pode sobreviver à
 * criação futura do slug. A proteção precisa estar na resposta final do rewrite,
 * sem alterar a política das páginas válidas.
 */
function negocioNaoEncontrado(request: NextRequest): NextResponse {
  const resposta = NextResponse.rewrite(new URL("/negocio-nao-encontrado", request.url));
  resposta.headers.set("Cache-Control", "no-store");
  return resposta;
}

/**
 * Link antigo de um negócio que trocou de slug (seção 5): por 90 dias, 301 para o mesmo caminho no endereço novo (links de
 * e-mail como /agendamentos/{token} continuam funcionando). Cache curto de propósito: um 301 fica guardado no navegador, e
 * depois do prazo o slug pode ser de outro negócio.
 */
async function redirecionarLinkAntigo(request: NextRequest, slug: string): Promise<NextResponse | null> {
  try {
    const resposta = await fetch(`${urlApiInterna}/publico/slugs-anteriores/${slug}`, {
      headers: { accept: "application/json" },
      cache: "no-store",
    });
    if (!resposta.ok) return null;

    const { slugAtual } = (await resposta.json()) as { slugAtual: string };
    if (!formatoDeSlugValido(slugAtual) || slugAtual === slug) return null;

    const destino = request.nextUrl.clone();
    destino.hostname = `${slugAtual}.${dominioBase}`;
    const redirecionamento = NextResponse.redirect(destino, 301);
    redirecionamento.headers.set("Cache-Control", "private, max-age=3600");
    return redirecionamento;
  } catch {
    return null;
  }
}

export async function proxy(request: NextRequest) {
  if (!dominioBase) {
    return NextResponse.next();
  }

  const host = (request.headers.get("host") ?? "").split(":")[0].toLowerCase();
  const sufixo = `.${dominioBase}`;

  // www → domínio raiz, mesmo caminho. Na Vercel isso era configuração do painel dela; em qualquer outra hospedagem
  // (Railway, VPS) sem esta linha o "www" cairia como slug de negócio e daria "negócio não encontrado".
  if (host === `www.${dominioBase}`) {
    const destino = request.nextUrl.clone();
    destino.hostname = dominioBase;
    return NextResponse.redirect(destino, 308);
  }

  const semSubdominioDeNegocio =
    host === dominioBase || host === `app.${dominioBase}` || !host.endsWith(sufixo);

  if (semSubdominioDeNegocio) {
    return NextResponse.next();
  }

  const slugCandidato = host.slice(0, -sufixo.length);

  if (!formatoDeSlugValido(slugCandidato)) {
    return negocioNaoEncontrado(request);
  }

  try {
    const resposta = await fetch(`${urlApiInterna}/publico/negocios-por-slug/${slugCandidato}`, {
      headers: { accept: "application/json" },
      cache: "no-store",
    });

    if (resposta.status === 404) {
      return (await redirecionarLinkAntigo(request, slugCandidato)) ?? negocioNaoEncontrado(request);
    }
  } catch {
    // API indisponível: não derruba o site inteiro por uma falha temporária de infra —
    // deixa passar em vez de mostrar 404 para um negócio que pode muito bem existir.
    return NextResponse.next();
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/((?!_next/static|_next/image|favicon.ico).*)"],
};
