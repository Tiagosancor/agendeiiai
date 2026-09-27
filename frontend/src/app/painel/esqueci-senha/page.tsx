"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { ErroApi, requisicaoApi } from "@/lib/api";
import { BotaoTema } from "@/components/BotaoTema";
import { classeBotaoPrimario, classeInput, classeLabel } from "@/components/estilos";

export default function PaginaEsqueciSenha() {
  const [email, setEmail] = useState("");
  const [enviado, setEnviado] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);

    try {
      await requisicaoApi("/painel/auth/esqueci-senha", { metodo: "POST", corpo: { email } });
      setEnviado(true);
    } catch (excecao) {
      setErro(
        excecao instanceof ErroApi && excecao.status === 429
          ? "Muitas tentativas. Aguarde um minuto e tente de novo."
          : "Não foi possível enviar agora. Tente novamente em instantes.",
      );
    } finally {
      setEnviando(false);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-gray-50 px-4 dark:bg-neutral-950">
      <BotaoTema className="fixed right-4 top-4" />
      <div className="w-full max-w-sm rounded-xl border border-gray-200 bg-white p-6 shadow-sm dark:border-neutral-800 dark:bg-neutral-900">
        <img src="/brand/agendeiiai-icone-reduzido.svg" alt="" width={40} height={40} className="mb-4 rounded-lg" />
        <h1 className="font-display mb-1 text-xl font-bold text-gray-900 dark:text-neutral-50">Esqueci minha senha</h1>

        {enviado ? (
          <>
            <p className="mb-6 text-sm text-gray-600 dark:text-neutral-300">
              Se <strong>{email}</strong> tiver acesso ao painel, você vai receber um e-mail com um link para criar uma
              senha nova. O link vale por 1 hora. Confira também a caixa de spam.
            </p>
            <Link href="/painel/login" className={`${classeBotaoPrimario} block w-full text-center`}>
              Voltar para a entrada
            </Link>
          </>
        ) : (
          <form onSubmit={aoEnviar}>
            <p className="mb-6 text-sm text-gray-500 dark:text-neutral-400">
              Informe o e-mail que você usa para entrar. Vamos mandar um link para criar uma senha nova.
            </p>

            <label className="mb-4 block text-sm">
              <span className={classeLabel}>E-mail</span>
              <input
                type="email"
                required
                autoComplete="username"
                value={email}
                onChange={(evento) => setEmail(evento.target.value)}
                className={classeInput}
              />
            </label>

            {erro && (
              <p role="alert" className="mb-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
                {erro}
              </p>
            )}

            <button type="submit" disabled={enviando} className={`${classeBotaoPrimario} w-full`}>
              {enviando ? "Enviando..." : "Enviar link"}
            </button>

            <p className="mt-4 text-center text-sm">
              <Link href="/painel/login" className="text-gray-600 underline dark:text-neutral-300">
                Voltar para a entrada
              </Link>
            </p>
          </form>
        )}
      </div>
    </main>
  );
}
