"use client";

/**
 * Cliente de API do painel. `NEXT_PUBLIC_API_URL` é embutida no bundle em tempo de build
 * (ver docs/decisoes.md) — é o endereço que o NAVEGADOR usa, diferente de
 * `API_URL_INTERNA` (usada só pelo servidor Next.js, em `proxy.ts`).
 */
const URL_BASE_API = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";

export class ErroApi extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}

interface OpcoesRequisicao {
  metodo?: "GET" | "POST" | "PUT" | "DELETE";
  corpo?: unknown;
  tokenAcesso?: string | null;
}

/**
 * Faz uma requisição ao painel. `credentials: "include"` é o que manda o cookie httpOnly
 * do refresh token nas chamadas de /painel/auth/* (seção 8.3.4) — para as demais rotas,
 * a API nem olha o cookie, só o header Authorization.
 */
export async function requisicaoApi<T>(caminho: string, opcoes: OpcoesRequisicao = {}): Promise<T> {
  const resposta = await fetch(`${URL_BASE_API}${caminho}`, {
    method: opcoes.metodo ?? "GET",
    credentials: "include",
    headers: {
      ...(opcoes.corpo !== undefined ? { "Content-Type": "application/json" } : {}),
      ...(opcoes.tokenAcesso ? { Authorization: `Bearer ${opcoes.tokenAcesso}` } : {}),
    },
    body: opcoes.corpo !== undefined ? JSON.stringify(opcoes.corpo) : undefined,
  });

  if (!resposta.ok) {
    let mensagem = `Erro ${resposta.status}`;
    try {
      const corpoErro = await resposta.json();
      mensagem = corpoErro.detail ?? corpoErro.title ?? mensagem;
    } catch {
      // corpo não é JSON (ex.: 401 sem corpo) — mantém a mensagem genérica.
    }
    throw new ErroApi(resposta.status, mensagem);
  }

  return lerCorpoJson<T>(resposta);
}

/** 204 (painel) e 202 Accepted (público, seção 8.1) não têm corpo — `.json()` direto quebraria neles. */
async function lerCorpoJson<T>(resposta: Response): Promise<T> {
  const texto = await resposta.text();
  return (texto ? JSON.parse(texto) : undefined) as T;
}

/**
 * Chamadas públicas (assistente de agendamento, seção 6.2) passam pelo proxy same-origin
 * `/api/publico/*` (`app/api/publico/[...caminho]/route.ts`), nunca direto em
 * `NEXT_PUBLIC_API_URL` — o navegador não consegue definir o cabeçalho `Host`, que é como
 * a API resolve o negócio nas rotas públicas (seção 8.3.2). `caminho` já vem com o prefixo
 * `/publico` retirado (ex.: `/servicos`, não `/publico/servicos`).
 */
export async function requisicaoApiPublica<T>(caminho: string, opcoes: OpcoesRequisicao = {}): Promise<T> {
  const resposta = await fetch(`/api/publico${caminho}`, {
    method: opcoes.metodo ?? "GET",
    headers: opcoes.corpo !== undefined ? { "Content-Type": "application/json" } : {},
    body: opcoes.corpo !== undefined ? JSON.stringify(opcoes.corpo) : undefined,
  });

  if (!resposta.ok) {
    let mensagem = `Erro ${resposta.status}`;
    try {
      const corpoErro = await resposta.json();
      mensagem = corpoErro.detail ?? corpoErro.title ?? mensagem;
    } catch {
      // corpo não é JSON — mantém a mensagem genérica.
    }
    throw new ErroApi(resposta.status, mensagem);
  }

  return lerCorpoJson<T>(resposta);
}
