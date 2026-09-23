import { NextRequest, NextResponse } from "next/server";

/**
 * Proxy same-origin para `POST /painel/auth/{login,renovar,logout}`. O refresh token vive
 * num cookie httpOnly emitido pela API (seção 8.3.4, `AutenticacaoController`). O problema:
 * o navegador chama a API diretamente (`lib/api.ts`, `requisicaoApi`), e o front e a API
 * ficam em hosts diferentes tanto no dev local quanto no "Caminho 1" de `docs/deploy.md`
 * (Vercel + Render) — aos olhos do navegador isso é cross-site, e `SameSite=Strict` nunca
 * manda o cookie de volta numa chamada `fetch` cross-site. Resultado: a sessão caía sozinha
 * a cada 15 min (expiração do access token, que tenta renovar em segundo plano) ou em
 * qualquer reload da página — achado e documentado no ajuste do "Replicar para..."
 * (CLAUDE.md, "Bugs conhecidos").
 *
 * Este proxy roda no servidor do Next.js: o navegador só conversa com o próprio domínio do
 * painel (nunca com o host da API), então o cookie de resposta nasce e volta host-only
 * NESSE domínio — exatamente o que o comentário original de `AutenticacaoController` já
 * pretendia — e `SameSite=Strict` volta a funcionar porque a chamada deixa de ser
 * cross-site em qualquer topologia de deploy (dev, Vercel+Render, VPS+Caddy).
 */
const urlApiInterna = process.env.API_URL_INTERNA ?? "http://localhost:5080";

export async function POST(request: NextRequest, contexto: { params: Promise<{ caminho: string[] }> }) {
  const { caminho } = await contexto.params;
  const destino = `${urlApiInterna}/painel/auth/${caminho.join("/")}`;

  const cookieDeEntrada = request.headers.get("cookie");

  const resposta = await fetch(destino, {
    method: "POST",
    headers: {
      "Content-Type": request.headers.get("content-type") ?? "application/json",
      ...(cookieDeEntrada ? { Cookie: cookieDeEntrada } : {}),
    },
    body: await request.text(),
    cache: "no-store",
  });

  const respostaProxy = new NextResponse(resposta.body, {
    status: resposta.status,
    headers: { "Content-Type": resposta.headers.get("content-type") ?? "application/json" },
  });

  // Só existe um Set-Cookie possível aqui (refresh_token) — repassa como veio da API.
  const cookieDeSaida = resposta.headers.get("set-cookie");
  if (cookieDeSaida) respostaProxy.headers.set("set-cookie", cookieDeSaida);

  return respostaProxy;
}
