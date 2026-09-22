"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { ErroApi, requisicaoApi } from "./api";

interface RespostaLogin {
  accessToken: string;
  expiraEm: string;
}

interface OpcoesChamada {
  metodo?: "GET" | "POST" | "PUT" | "DELETE";
  corpo?: unknown;
}

interface ContextoAutenticacao {
  carregando: boolean;
  autenticado: boolean;
  entrar: (email: string, senha: string) => Promise<void>;
  sair: () => Promise<void>;
  /** Chama a API do painel com o token de acesso atual; se der 401, tenta renovar uma vez antes de desistir. */
  chamarApi: <T>(caminho: string, opcoes?: OpcoesChamada) => Promise<T>;
}

const Contexto = createContext<ContextoAutenticacao | null>(null);

export function ProvedorAutenticacao({ children }: { children: ReactNode }) {
  const [tokenAcesso, setTokenAcesso] = useState<string | null>(null);
  const [carregando, setCarregando] = useState(true);

  // O access token some ao recarregar a página (fica só em memória — nunca em
  // localStorage/sessionStorage, que ficam acessíveis a qualquer script). Ao montar,
  // tenta renovar a partir do cookie httpOnly do refresh token (seção 8.3.4).
  const renovar = useCallback(async (): Promise<string | null> => {
    try {
      const resposta = await requisicaoApi<RespostaLogin>("/painel/auth/renovar", { metodo: "POST" });
      setTokenAcesso(resposta.accessToken);
      return resposta.accessToken;
    } catch {
      setTokenAcesso(null);
      return null;
    }
  }, []);

  useEffect(() => {
    // Busca disparada pela montagem (tenta renovar a sessão a partir do cookie), não estado derivado de props.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    renovar().finally(() => setCarregando(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const entrar = useCallback(async (email: string, senha: string) => {
    const resposta = await requisicaoApi<RespostaLogin>("/painel/auth/login", {
      metodo: "POST",
      corpo: { email, senha },
    });
    setTokenAcesso(resposta.accessToken);
  }, []);

  const sair = useCallback(async () => {
    try {
      await requisicaoApi("/painel/auth/logout", { metodo: "POST" });
    } finally {
      setTokenAcesso(null);
    }
  }, []);

  const chamarApi = useCallback(
    async <T,>(caminho: string, opcoes: OpcoesChamada = {}): Promise<T> => {
      try {
        return await requisicaoApi<T>(caminho, { ...opcoes, tokenAcesso });
      } catch (erro) {
        if (erro instanceof ErroApi && erro.status === 401) {
          const novoToken = await renovar();
          if (novoToken) {
            return await requisicaoApi<T>(caminho, { ...opcoes, tokenAcesso: novoToken });
          }
        }
        throw erro;
      }
    },
    [tokenAcesso, renovar],
  );

  const valor = useMemo<ContextoAutenticacao>(
    () => ({ carregando, autenticado: tokenAcesso !== null, entrar, sair, chamarApi }),
    [carregando, tokenAcesso, entrar, sair, chamarApi],
  );

  return <Contexto.Provider value={valor}>{children}</Contexto.Provider>;
}

export function useAutenticacao(): ContextoAutenticacao {
  const contexto = useContext(Contexto);
  if (!contexto) throw new Error("useAutenticacao precisa ser usado dentro de <ProvedorAutenticacao>.");
  return contexto;
}

export { ErroApi };
