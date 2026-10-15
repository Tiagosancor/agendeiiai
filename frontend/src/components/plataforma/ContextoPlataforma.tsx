"use client";

import { createContext, useCallback, useContext, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { CampoSenha } from "@/components/CampoSenha";
import { classeBotaoPrimario, classeInput, classeLabel } from "@/components/estilos";
import { ErroApi, requisicaoApi } from "@/lib/api";

interface OpcoesChamadaPlataforma {
  metodo?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  corpo?: unknown;
  sinal?: AbortSignal;
}

export type ChamarPlataforma = <T>(caminho: string, opcoes?: OpcoesChamadaPlataforma) => Promise<T>;

interface ContextoPlataformaValor {
  token: string;
  chamar: ChamarPlataforma;
  sair: () => void;
}

const ContextoPlataforma = createContext<ContextoPlataformaValor | null>(null);

export function ProvedorPlataforma({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(null);

  const sair = useCallback(() => setToken(null), []);
  const chamar = useCallback<ChamarPlataforma>(
    async (caminho, opcoes = {}) => {
      try {
        return await requisicaoApi(caminho, {
          metodo: opcoes.metodo,
          corpo: opcoes.corpo,
          sinal: opcoes.sinal,
          tokenAcesso: token,
        });
      } catch (erro) {
        if (erro instanceof ErroApi && erro.status === 401) sair();
        throw erro;
      }
    },
    [sair, token],
  );

  const valor = useMemo(() => (token ? { token, chamar, sair } : null), [chamar, sair, token]);

  if (!valor) return <LoginPlataforma aoEntrar={setToken} />;
  return <ContextoPlataforma.Provider value={valor}>{children}</ContextoPlataforma.Provider>;
}

export function usePlataforma(): ContextoPlataformaValor {
  const contexto = useContext(ContextoPlataforma);
  if (!contexto) throw new Error("usePlataforma deve ser usado dentro de ProvedorPlataforma.");
  return contexto;
}

function LoginPlataforma({ aoEntrar }: { aoEntrar: (token: string) => void }) {
  const [email, setEmail] = useState("");
  const [senha, setSenha] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      const sessao = await requisicaoApi<{ accessToken: string }>("/plataforma/auth/login", {
        metodo: "POST",
        corpo: { email, senha },
      });
      aoEntrar(sessao.accessToken);
    } catch (excecao) {
      setErro(excecao instanceof ErroApi && excecao.status === 429 ? "Muitas tentativas. Aguarde um minuto." : "E-mail ou senha incorretos.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center px-4">
      <form onSubmit={enviar} className="w-full max-w-sm space-y-3 rounded-xl border border-gray-200 bg-white p-6 dark:border-neutral-800 dark:bg-neutral-900">
        <h1 className="font-display text-xl font-bold">Administração da plataforma</h1>
        <label className="block">
          <span className={classeLabel}>E-mail</span>
          <input type="email" required className={classeInput} value={email} onChange={(evento) => setEmail(evento.target.value)} autoComplete="username" />
        </label>
        <div>
          <CampoSenha rotulo="Senha" required className={classeInput} value={senha} onChange={(evento) => setSenha(evento.target.value)} autoComplete="current-password" />
        </div>
        {erro && <p role="alert" className="text-sm text-red-700 dark:text-red-400">{erro}</p>}
        <button type="submit" disabled={enviando} className={`${classeBotaoPrimario} min-h-11 w-full`}>
          {enviando ? "Entrando..." : "Entrar"}
        </button>
      </form>
    </main>
  );
}
