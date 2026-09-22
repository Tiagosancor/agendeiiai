"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";

export default function PaginaLogin() {
  const { entrar } = useAutenticacao();
  const roteador = useRouter();

  const [email, setEmail] = useState("");
  const [senha, setSenha] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);

    try {
      await entrar(email, senha);
      roteador.push("/painel");
    } catch (excecao) {
      if (excecao instanceof ErroApi && excecao.status === 401) {
        setErro("E-mail ou senha incorretos.");
      } else {
        setErro("Não foi possível entrar. Tente novamente em instantes.");
      }
    } finally {
      setEnviando(false);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-gray-50 px-4 dark:bg-neutral-950">
      <form
        onSubmit={aoEnviar}
        className="w-full max-w-sm rounded-xl border border-gray-200 bg-white p-6 shadow-sm dark:border-neutral-800 dark:bg-neutral-900"
      >
        <h1 className="mb-1 text-xl font-semibold text-gray-900 dark:text-neutral-50">Entrar no painel</h1>
        <p className="mb-6 text-sm text-gray-500 dark:text-neutral-400">Acesse com o e-mail e a senha do seu negócio.</p>

        <label className="mb-3 block text-sm">
          <span className="mb-1 block font-medium text-gray-700 dark:text-neutral-300">E-mail</span>
          <input
            type="email"
            required
            autoComplete="username"
            value={email}
            onChange={(evento) => setEmail(evento.target.value)}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500 dark:border-neutral-700 dark:bg-neutral-800 dark:text-neutral-50"
          />
        </label>

        <label className="mb-4 block text-sm">
          <span className="mb-1 block font-medium text-gray-700 dark:text-neutral-300">Senha</span>
          <input
            type="password"
            required
            autoComplete="current-password"
            value={senha}
            onChange={(evento) => setSenha(evento.target.value)}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500 dark:border-neutral-700 dark:bg-neutral-800 dark:text-neutral-50"
          />
        </label>

        {erro && (
          <p className="mb-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
            {erro}
          </p>
        )}

        <button
          type="submit"
          disabled={enviando}
          className="w-full rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-60"
        >
          {enviando ? "Entrando..." : "Entrar"}
        </button>
      </form>
    </main>
  );
}
