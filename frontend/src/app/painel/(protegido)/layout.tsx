"use client";

import { useEffect } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useAutenticacao } from "@/lib/auth-context";
import { BotaoTema } from "@/components/BotaoTema";
import { AvisoAssinatura } from "@/components/painel/AvisoAssinatura";

const ITENS_MENU = [
  { href: "/painel", rotulo: "Início" },
  { href: "/painel/agenda", rotulo: "Agenda" },
  { href: "/painel/usuarios", rotulo: "Usuários" },
  { href: "/painel/profissionais", rotulo: "Profissionais" },
  { href: "/painel/servicos", rotulo: "Serviços" },
  { href: "/painel/cupons", rotulo: "Cupons" },
  { href: "/painel/clientes", rotulo: "Clientes" },
  { href: "/painel/financeiro", rotulo: "Financeiro" },
  { href: "/painel/fidelidade", rotulo: "Fidelidade" },
  { href: "/painel/negocio", rotulo: "Meu negócio" },
  { href: "/painel/assinatura", rotulo: "Assinatura" },
];

export default function LayoutProtegido({ children }: { children: React.ReactNode }) {
  const { autenticado, carregando, sair } = useAutenticacao();
  const roteador = useRouter();
  const caminhoAtual = usePathname();

  useEffect(() => {
    if (!carregando && !autenticado) {
      roteador.replace("/painel/login");
    }
  }, [carregando, autenticado, roteador]);

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
    <div className="min-h-screen bg-gray-50 dark:bg-neutral-950">
      <header className="border-b border-gray-200 bg-white dark:border-neutral-800 dark:bg-neutral-900">
        <div className="mx-auto flex max-w-5xl items-center justify-between px-4 py-3">
          <span className="flex items-center gap-2">
            {/* Marca do produto (seção 5.1) — nunca a marca do negócio, que fica só na
                página pública de cada tenant. "Agendeiiai" sem acento: é UI/código. */}
            <img src="/brand/agendeiiai-icone-reduzido.svg" alt="" width={24} height={24} className="rounded-md" />
            <span className="font-display text-sm font-bold text-gray-900 dark:text-neutral-50">Agendeiiai</span>
          </span>
          <div className="flex items-center gap-1">
            <BotaoTema />
            <button
              onClick={async () => {
                await sair();
                roteador.replace("/painel/login");
              }}
              className="text-sm text-gray-500 hover:text-gray-800 dark:text-neutral-400 dark:hover:text-neutral-100"
            >
              Sair
            </button>
          </div>
        </div>
        <nav className="mx-auto max-w-5xl overflow-x-auto px-4 pb-2">
          <ul className="flex gap-4 text-sm whitespace-nowrap">
            {ITENS_MENU.map((item) => (
              <li key={item.href}>
                <Link
                  href={item.href}
                  className={
                    caminhoAtual === item.href
                      ? "font-medium text-marca-primaria dark:text-marca-acento"
                      : "text-gray-500 hover:text-gray-800 dark:text-neutral-400 dark:hover:text-neutral-100"
                  }
                >
                  {item.rotulo}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      </header>

      <AvisoAssinatura />

      <main className="mx-auto max-w-5xl px-4 py-6">{children}</main>
    </div>
  );
}
