import { NextRequest, NextResponse } from "next/server";
import { extrairSlugDoHost } from "@/lib/dominio";

/**
 * Proxy same-origin para `/publico/*` da API (seção 8.3.2/6). O navegador NUNCA consegue
 * definir o cabeçalho `Host` de um `fetch()` (é um cabeçalho proibido pela própria spec)
 * — então uma chamada direta do cliente para `NEXT_PUBLIC_API_URL/publico/...` chegaria na
 * API com `Host: localhost:5080` (ou o que for a API_URL), nunca com o subdomínio do
 * negócio, e o `ResolucaoNegocioMiddleware` (que resolve o tenant público pelo Host, nunca
 * por entrada do cliente) não resolveria negócio nenhum.
 *
 * Este proxy roda no servidor do Next.js, que recebe a requisição do navegador no MESMO
 * host que carregou a página (`{slug}.{dominio}:3000` — sem CORS, sem o problema do
 * cabeçalho proibido). A princípio bastaria reencaminhar pra API interna repassando esse
 * Host original — mas o `fetch()` do Node (undici) também ignora silenciosamente um
 * `Host` setado manualmente, sobrescrevendo com o da URL de destino (testado manualmente).
 * Por isso o slug é calculado aqui a partir do Host real (a mesma fonte confiável de
 * sempre) e mandado no cabeçalho `X-Slug-Negocio`, que o middleware da API aceita como
 * alternativa só quando o Host da chamada em si não resolveu nenhum tenant.
 */
const urlApiInterna = process.env.API_URL_INTERNA ?? "http://localhost:5080";

async function encaminhar(request: NextRequest, contexto: { params: Promise<{ caminho: string[] }> }) {
  const { caminho } = await contexto.params;
  const destino = new URL(`${urlApiInterna}/publico/${caminho.join("/")}${request.nextUrl.search}`);

  const dominioBase = process.env.MARCA_DOMINIO ?? "";
  const slug = dominioBase ? extrairSlugDoHost(request.headers.get("host") ?? "", dominioBase) : null;

  const resposta = await fetch(destino, {
    method: request.method,
    headers: {
      "Content-Type": request.headers.get("content-type") ?? "application/json",
      ...(slug ? { "X-Slug-Negocio": slug } : {}),
    },
    body: request.method === "GET" || request.method === "HEAD" ? undefined : await request.text(),
    cache: "no-store",
  });

  return new NextResponse(resposta.body, {
    status: resposta.status,
    headers: { "Content-Type": resposta.headers.get("content-type") ?? "application/json" },
  });
}

export { encaminhar as GET, encaminhar as POST, encaminhar as PUT, encaminhar as DELETE };
