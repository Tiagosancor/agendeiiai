"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import type { MinhaConta } from "./tipos";
import { ErroApi, requisicaoApi, requisicaoAutenticacaoPainel } from "./api";

interface RespostaLogin {
  accessToken: string;
  expiraEm: string;
}

interface OpcoesChamada {
  metodo?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  corpo?: unknown;
  cabecalhos?: Record<string, string>;
  sinal?: AbortSignal;
  /** Consulta de fundo (ex.: contador do menu): num 401 não renova a sessão, senão manter a aba aberta a prolonga para sempre. */
  semRenovar?: boolean;
}

interface ContextoAutenticacao {
  usuarioId: string | null;
  minhaConta: MinhaConta | null;
  atualizarMinhaConta: () => Promise<void>;
  carregando: boolean;
  autenticado: boolean;
  entrar: (identificador: string, senha: string) => Promise<void>;
  sair: () => Promise<void>;
  /** Chama a API do painel com o token de acesso atual; se der 401, tenta renovar uma vez antes de desistir. */
  chamarApi: <T>(caminho: string, opcoes?: OpcoesChamada) => Promise<T>;
  /**
   * Permissões do usuário logado, lidas do próprio token (claim "permissao"). Só para
   * esconder o que ele não pode fazer — quem garante a regra é sempre a API (403).
   */
  temPermissao: (permissao: string) => boolean;
}

/** Lê as claims do JWT sem validar (a validação é da API) — só para a interface. */
function permissoesDoToken(token: string | null): string[] {
  if (!token) return [];
  try {
    const base64 = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
    const json = decodeURIComponent(
      atob(base64)
        .split("")
        .map((c) => "%" + c.charCodeAt(0).toString(16).padStart(2, "0"))
        .join(""),
    );
    const permissao = JSON.parse(json).permissao;
    return Array.isArray(permissao) ? permissao : permissao ? [permissao] : [];
  } catch {
    return [];
  }
}

const Contexto = createContext<ContextoAutenticacao | null>(null);

export function ProvedorAutenticacao({ children }: { children: ReactNode }) {
  const [minhaConta, setMinhaConta] = useState<MinhaConta | null>(null);
  const [versaoLogin, setVersaoLogin] = useState(0);
  const [tokenAcesso, setTokenAcesso] = useState<string | null>(null);
  const roteador = useRouter();
  const [carregando, setCarregando] = useState(true);

  // O access token some ao recarregar a página (fica só em memória — nunca em
  // localStorage/sessionStorage, que ficam acessíveis a qualquer script). Ao montar,
  // tenta renovar a partir do cookie httpOnly do refresh token (seção 8.3.4).
  const renovar = useCallback(async (): Promise<string | null> => {
    try {
      const resposta = await requisicaoAutenticacaoPainel<RespostaLogin>("renovar");
      setTokenAcesso(resposta.accessToken);
      return resposta.accessToken;
    } catch {
      setTokenAcesso(null);
      return null;
    }
  }, []);

  useEffect(() => {
    // Busca disparada pela montagem (tenta renovar a sessão a partir do cookie), não estado derivado de props.
    renovar().finally(() => setCarregando(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const entrar = useCallback(async (identificador: string, senha: string) => {
    const resposta = await requisicaoAutenticacaoPainel<RespostaLogin>("login", { identificador, senha });
    setTokenAcesso(resposta.accessToken);
    // Mesmo que dois logins emitam o mesmo JWT no mesmo segundo, recarrega o cadastro.
    setVersaoLogin((versao) => versao + 1);
  }, []);

  const sair = useCallback(async () => {
    try {
      await requisicaoAutenticacaoPainel("logout");
    } finally {
      setMinhaConta(null);
      setTokenAcesso(null);
    }
  }, []);

  const chamarApi = useCallback(
    async <T,>(caminho: string, { semRenovar, ...opcoes }: OpcoesChamada = {}): Promise<T> => {
      try {
        return await requisicaoApi<T>(caminho, { ...opcoes, tokenAcesso });
      } catch (erro) {
        // Assinatura suspensa (seção 7): a API só libera a tela de assinatura — leva direto pra lá.
        if (erro instanceof ErroApi && erro.codigo === "assinatura_suspensa" && !["/painel/assinatura", "/painel/minha-conta"].includes(window.location.pathname)) {
          roteador.push("/painel/assinatura");
        }
        if (erro instanceof ErroApi && erro.status === 401 && !semRenovar) {
          const novoToken = await renovar();
          if (novoToken) {
            return await requisicaoApi<T>(caminho, { ...opcoes, tokenAcesso: novoToken });
          }
        }
        throw erro;
      }
    },
    [tokenAcesso, renovar, roteador],
  );

  const atualizarMinhaConta = useCallback(async () => {
    setMinhaConta(await chamarApi<MinhaConta>("/painel/minha-conta"));
  }, [chamarApi]);

  useEffect(() => {
    if (!tokenAcesso) return;
    const controle = new AbortController();
    requisicaoApi<MinhaConta>("/painel/minha-conta", { tokenAcesso, sinal: controle.signal })
      .then(setMinhaConta).catch(() => { if (!controle.signal.aborted) setMinhaConta(null); });
    return () => controle.abort();
  }, [tokenAcesso, versaoLogin]);

  const valor = useMemo<ContextoAutenticacao>(() => {
    const permissoes = permissoesDoToken(tokenAcesso);
    let usuarioId: string | null = null;
    try { if (tokenAcesso) usuarioId = JSON.parse(atob(tokenAcesso.split(".")[1].replace(/-/g, "+").replace(/_/g, "/"))).sub ?? null; } catch { /* Token inválido: a API decide a autorização. */ }
    return {
      usuarioId,
      minhaConta: tokenAcesso ? minhaConta : null,
      atualizarMinhaConta,
      carregando,
      autenticado: tokenAcesso !== null,
      entrar,
      sair,
      chamarApi,
      temPermissao: (permissao: string) => permissoes.includes(permissao),
    };
  }, [carregando, tokenAcesso, entrar, sair, chamarApi, minhaConta, atualizarMinhaConta]);

  return <Contexto.Provider value={valor}>{children}</Contexto.Provider>;
}

export function useAutenticacao(): ContextoAutenticacao {
  const contexto = useContext(Contexto);
  if (!contexto) throw new Error("useAutenticacao precisa ser usado dentro de <ProvedorAutenticacao>.");
  return contexto;
}

export { ErroApi };
