"use client";

import { Suspense, useEffect, useState, type FormEvent } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { ErroApi, requisicaoApi } from "@/lib/api";
import { BotaoTema } from "@/components/BotaoTema";
import { MarcaPainel } from "@/components/painel/IdentidadePainel";
import "@/components/painel/painel-recuperacao.css";
import { CampoSenha } from "@/components/CampoSenha";
import { classeBotaoPrimario, classeInput } from "@/components/estilos";

const senhaForte = (senha: string) => senha.length >= 8 && /\p{L}/u.test(senha) && /\d/.test(senha);

/** Destino do link de "esqueci minha senha" (`?token=...`, enviado por e-mail). */
export default function PaginaRedefinirSenha() {
  return (
    <main className="painel-login painel-recuperacao">
      <BotaoTema className="painel-login-tema painel-tema" />
      <div className="painel-login-card">
        <MarcaPainel tagline />
        <h1 className="font-display mb-1 text-xl font-bold text-gray-900 dark:text-neutral-50">Criar senha nova</h1>
        <Suspense>
          <FormularioRedefinicao />
        </Suspense>
      </div>
    </main>
  );
}

function FormularioRedefinicao() {
  const parametros = useSearchParams();
  const [token] = useState(() => parametros.get("token") ?? "");
  const [senha, setSenha] = useState("");
  const [concluido, setConcluido] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  // Tira o token da barra de endereço (histórico, compartilhamento de tela) depois de lido.
  useEffect(() => {
    if (token) window.history.replaceState(null, "", window.location.pathname);
  }, [token]);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);

    if (!senhaForte(senha)) {
      setErro("A senha precisa ter pelo menos 8 caracteres, com letras e números.");
      return;
    }

    setEnviando(true);
    try {
      await requisicaoApi("/painel/auth/redefinir-senha", { metodo: "POST", corpo: { token, novaSenha: senha } });
      setConcluido(true);
    } catch (excecao) {
      if (excecao instanceof ErroApi && excecao.status === 400) setErro(excecao.message);
      else if (excecao instanceof ErroApi && excecao.status === 429) setErro("Muitas tentativas. Aguarde um minuto e tente de novo.");
      else setErro("Não foi possível salvar agora. Tente novamente em instantes.");
    } finally {
      setEnviando(false);
    }
  }

  if (!token) {
    return (
      <>
        <p className="mb-6 text-sm text-gray-600 dark:text-neutral-300">
          Este link está incompleto. Abra de novo o link do e-mail ou peça um novo.
        </p>
        <Link href="/painel/esqueci-senha" className={`${classeBotaoPrimario} painel-botao-principal painel-recuperacao-acao w-full`}>
          Pedir um link novo
        </Link>
      </>
    );
  }

  if (concluido) {
    return (
      <>
        <p className="mb-6 text-sm text-gray-600 dark:text-neutral-300">
          Senha alterada. Entre com a senha nova — por segurança, as outras sessões abertas foram encerradas.
        </p>
        <Link href="/painel/login" className={`${classeBotaoPrimario} painel-botao-principal painel-recuperacao-acao w-full`}>
          Ir para a entrada
        </Link>
      </>
    );
  }

  return (
    <form onSubmit={aoEnviar}>
      <p className="mb-6 text-sm text-gray-500 dark:text-neutral-400">Escolha a senha que você vai usar para entrar no painel.</p>

      <div className="mb-4 text-sm">
        <CampoSenha
          rotulo="Senha nova"
          ajuda="Pelo menos 8 caracteres, com letras e números."
          required
          autoFocus
          autoComplete="new-password"
          value={senha}
          onChange={(evento) => setSenha(evento.target.value)}
          className={classeInput}
        />
      </div>

      {erro && (
        <p role="alert" className="mb-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {erro}
        </p>
      )}

      <button type="submit" disabled={enviando} className={`${classeBotaoPrimario} painel-botao-principal w-full`}>
        {enviando ? "Salvando..." : "Salvar senha nova"}
      </button>

      <p className="mt-4 text-center text-sm">
        <Link href="/painel/esqueci-senha" className="text-gray-600 underline dark:text-neutral-300">
          Pedir um link novo
        </Link>
      </p>
    </form>
  );
}
