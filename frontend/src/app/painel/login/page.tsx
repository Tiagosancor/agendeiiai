"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { BotaoTema } from "@/components/BotaoTema";
import { MarcaPainel } from "@/components/painel/IdentidadePainel";
import { CampoSenha } from "@/components/CampoSenha";
import { lerUltimaRota } from "@/lib/ultima-rota";

export default function PaginaLogin() {
  const { entrar, autenticado, carregando } = useAutenticacao();
  const roteador = useRouter();

  // O PWA do painel abre aqui (manifest, seção 5): com a sessão ainda válida, volta para a última tela aberta.
  // Quem acabou de entrar pelo formulário segue para o início (aoEnviar), não para a tela de uma sessão anterior.
  const entrandoAgora = useRef(false);
  useEffect(() => {
    if (!carregando && autenticado && !entrandoAgora.current) roteador.replace(lerUltimaRota());
  }, [carregando, autenticado, roteador]);

  const [email, setEmail] = useState("");
  const [senha, setSenha] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);

    entrandoAgora.current = true;
    try {
      await entrar(email, senha);
      roteador.push("/painel");
    } catch (excecao) {
      entrandoAgora.current = false;
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
    <main className="painel-login">
      <BotaoTema className="painel-login-tema painel-tema" />
      <form
        onSubmit={aoEnviar}
        className="painel-login-card"
      >
        <MarcaPainel tagline />
        <h1 className="font-display mb-1 text-xl font-bold text-gray-900 dark:text-neutral-50">Entrar no painel</h1>
        <p className="mb-6 text-sm text-gray-500 dark:text-neutral-400">Acesse com o e-mail e a senha do seu negócio.</p>

        <label className="mb-3 block text-sm">
          <span className="mb-1 block font-medium text-gray-700 dark:text-neutral-300">E-mail</span>
          <input
            type="email"
            required
            autoComplete="username"
            value={email}
            onChange={(evento) => setEmail(evento.target.value)}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm outline-none focus:border-marca-primaria focus:ring-1 focus:ring-marca-primaria dark:focus:border-marca-acento dark:focus:ring-marca-acento dark:border-neutral-700 dark:bg-neutral-800 dark:text-neutral-50"
          />
        </label>

        <div className="mb-4 text-sm">
          <CampoSenha
            rotulo="Senha"
            classeRotulo="mb-1 block font-medium text-gray-700 dark:text-neutral-300"
            required
            autoComplete="current-password"
            value={senha}
            onChange={(evento) => setSenha(evento.target.value)}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm outline-none focus:border-marca-primaria focus:ring-1 focus:ring-marca-primaria dark:focus:border-marca-acento dark:focus:ring-marca-acento dark:border-neutral-700 dark:bg-neutral-800 dark:text-neutral-50"
          />
        </div>

        {erro && (
          <p role="alert" className="mb-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
            {erro}
          </p>
        )}

        <button
          type="submit"
          disabled={enviando}
          className="painel-botao-principal w-full rounded-lg bg-marca-primaria px-3 py-2 text-sm font-medium text-white transition hover:bg-marca-primaria-hover disabled:opacity-60"
        >
          {enviando ? "Entrando..." : "Entrar"}
        </button>

        <p className="mt-4 text-center text-sm">
          <Link href="/painel/esqueci-senha" className="text-gray-600 underline dark:text-neutral-300">
            Esqueci minha senha
          </Link>
        </p>
      </form>
    </main>
  );
}
