"use client";

import { useEffect } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useAutenticacao } from "@/lib/auth-context";
import { AppShellPainel } from "@/components/painel/AppShellPainel";
import { AvisoAssinatura } from "@/components/painel/AvisoAssinatura";
import { guardarUltimaRota } from "@/lib/ultima-rota";

export default function LayoutProtegido({ children }: { children: React.ReactNode }) {
  const { autenticado, carregando, sair } = useAutenticacao();
  const roteador = useRouter();
  const caminhoAtual = usePathname();

  useEffect(() => {
    if (!carregando && !autenticado) {
      roteador.replace("/painel/login");
    }
  }, [carregando, autenticado, roteador]);

  useEffect(() => {
    if (autenticado) guardarUltimaRota(caminhoAtual);
  }, [autenticado, caminhoAtual]);

  if (carregando) {
    return (
      <main className="flex min-h-screen items-center justify-center text-sm text-gray-500 dark:text-neutral-400">
        Carregando...
      </main>
    );
  }

  if (!autenticado) {
    // Evita piscar o conteúdo protegido antes do redirecionamento do efeito acima.
    return null;
  }

  return (
    <AppShellPainel aoSair={async () => {
      await sair();
      roteador.replace("/painel/login");
    }}>
      <AvisoAssinatura />
      {children}
    </AppShellPainel>
  );
}
