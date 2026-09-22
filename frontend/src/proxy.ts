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

export async function proxy(request: NextRequest) {
  if (!dominioBase) {
    return NextResponse.next();
  }

  const host = (request.headers.get("host") ?? "").split(":")[0].toLowerCase();
  const sufixo = `.${dominioBase}`;

  const semSubdominioDeNegocio =
    host === dominioBase || host === `app.${dominioBase}` || !host.endsWith(sufixo);

  if (semSubdominioDeNegocio) {
    return NextResponse.next();
  }

  const slugCandidato = host.slice(0, -sufixo.length);

  if (!formatoDeSlugValido(slugCandidato)) {
    return NextResponse.rewrite(new URL("/negocio-nao-encontrado", request.url));
  }

  try {
    const resposta = await fetch(`${urlApiInterna}/publico/negocios-por-slug/${slugCandidato}`, {
      headers: { accept: "application/json" },
      cache: "no-store",
    });

    if (resposta.status === 404) {
      return NextResponse.rewrite(new URL("/negocio-nao-encontrado", request.url));
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
