"use client";

import { CabecalhoCadastro } from "@/components/painel/CadastrosVisuais";
import { LembreteRetorno } from "@/components/painel/LembreteRetorno";
import { useAutenticacao } from "@/lib/auth-context";
import "@/components/painel/painel-negocio.css";

export default function PaginaNotificacoes() {
  const { temPermissao } = useAutenticacao();

  if (!temPermissao("GerenciarConfiguracoesDoNegocio")) {
    return <p className="text-sm text-gray-500 dark:text-neutral-400">Sem permissão para configurar as notificações do negócio.</p>;
  }

  return (
    <div className="painel-cadastro painel-negocio painel-notificacoes">
      <CabecalhoCadastro
        titulo="Notificações"
        descricao="Configure as mensagens automáticas enviadas aos seus clientes."
      />
      <LembreteRetorno />
    </div>
  );
}
