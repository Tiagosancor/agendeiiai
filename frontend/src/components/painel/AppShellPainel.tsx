"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { BotaoTema } from "@/components/BotaoTema";
import { MarcaPainel } from "./IdentidadePainel";

// As mesmas rotas do layout anterior. Autorizações continuam nas telas e na API.
const ITENS_MENU = [
  ["/painel", "Início", "M3 10l9-7 9 7v10H3zM9 20v-7h6v7"],
  ["/painel/agenda", "Agenda", "M5 5h14v15H5zM8 2v6M16 2v6M5 10h14M8 14h3"],
  ["/painel/usuarios", "Usuários", "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M19 8a4 4 0 0 1 0 8M22 21v-2M13 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0"],
  ["/painel/profissionais", "Profissionais", "M20 21v-2a8 8 0 0 0-16 0M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0"],
  ["/painel/servicos", "Serviços", "M3 7h18v13H3zM8 7V3h8v4M3 12h18M10 12v3h4v-3"],
  ["/painel/cupons", "Cupons", "M3 3h8l10 10-8 8L3 11zM7 7h.01"],
  ["/painel/clientes", "Clientes", "M20 21v-2a8 8 0 0 0-16 0M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0"],
  ["/painel/financeiro", "Financeiro", "M3 21h18M6 17v-6M12 17V7M18 17V3M3 7l5-4 5 2 7-4"],
  ["/painel/comissoes", "Comissões", "M3 6h18v14H3zM3 10h18M8 15h3M7 3v3M17 3v3"],
  ["/painel/fidelidade", "Fidelidade", "M3 8h18v4H3zM5 12v9h14v-9M12 8v13M12 8C5 8 4 2 8 2c3 0 4 6 4 6s1-6 4-6c4 0 3 6-4 6"],
  ["/painel/vendas", "Vendas", "M3 3h2l3 13h12l2-9H6M10 21h.01M18 21h.01"],
  ["/painel/estoque", "Estoque", "M3 7l9-5 9 5v10l-9 5-9-5zM3 7l9 5 9-5M12 12v10M7 4l10 6"],
  ["/painel/negocio", "Meu negócio", "M3 9l2-6h14l2 6M3 9v4h18V9M5 13v8h14v-8M9 21v-6h6v6"],
  ["/painel/assinatura", "Assinatura", "M3 5h18v14H3zM3 10h18M7 15h3"],
] as const;

export function AppShellPainel({ children, aoSair }: { children: ReactNode; aoSair: () => Promise<void> }) {
  const caminho = usePathname();
  const [aberto, setAberto] = useState(false);
  const sidebar = useRef<HTMLElement>(null);
  const botaoMenu = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!aberto) return;
    const painel = sidebar.current;
    const controleOrigem = botaoMenu.current;
    const overflowAnterior = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    // Aguarda o drawer ficar visível antes de transferir o foco.
    const quadro = requestAnimationFrame(() => painel?.querySelector<HTMLButtonElement>("button")?.focus());
    function teclado(evento: KeyboardEvent) {
      if (evento.key === "Escape") setAberto(false);
      if (evento.key !== "Tab" || !painel) return;
      const controles = Array.from(painel.querySelectorAll<HTMLElement>("a[href], button:not(:disabled)"));
      const primeiro = controles[0];
      const ultimo = controles.at(-1);
      if (!painel.contains(document.activeElement)) { evento.preventDefault(); (evento.shiftKey ? ultimo : primeiro)?.focus(); }
      else if (evento.shiftKey && document.activeElement === primeiro) { evento.preventDefault(); ultimo?.focus(); }
      else if (!evento.shiftKey && document.activeElement === ultimo) { evento.preventDefault(); primeiro?.focus(); }
    }
    const desktop = window.matchMedia("(min-width: 1024px)");
    function redimensionar() { if (desktop.matches) setAberto(false); }
    document.addEventListener("keydown", teclado);
    desktop.addEventListener("change", redimensionar);
    return () => {
      document.body.style.overflow = overflowAnterior;
      cancelAnimationFrame(quadro);
      document.removeEventListener("keydown", teclado);
      desktop.removeEventListener("change", redimensionar);
      controleOrigem?.focus();
    };
  }, [aberto]);

  return <div className="painel-shell">
    <a className="painel-pular" href="#conteudo-painel" inert={aberto}>Ir para o conteúdo</a>
    <header className="painel-header-mobile" inert={aberto}>
      <button ref={botaoMenu} type="button" className="painel-menu-controle" aria-label="Abrir menu" aria-expanded={aberto} aria-controls="navegacao-painel" onClick={() => setAberto(true)}>
        <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true"><path d="M4 6h16M4 12h16M4 18h16" /></svg>
      </button>
      <MarcaPainel />
    </header>
    {aberto && <div className="painel-menu-fundo" onClick={() => setAberto(false)} aria-hidden="true" />}
    <aside ref={sidebar} id="navegacao-painel" className={`painel-sidebar ${aberto ? "painel-sidebar-aberta" : ""}`} role={aberto ? "dialog" : undefined} aria-modal={aberto ? true : undefined} aria-label="Menu principal">
      <div className="painel-sidebar-marca"><MarcaPainel /><button type="button" className="painel-menu-controle painel-menu-fechar" aria-label="Fechar menu" onClick={() => setAberto(false)}>✕</button></div>
      <nav aria-label="Navegação do painel"><ul>{ITENS_MENU.map(([href, rotulo, desenho]) => {
        const ativo = caminho === href || (href !== "/painel" && caminho.startsWith(`${href}/`));
        return <li key={href}><Link href={href} aria-current={ativo ? "page" : undefined} onClick={() => setAberto(false)}>
          <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={desenho} /></svg><span>{rotulo}</span>
        </Link></li>;
      })}</ul></nav>
      <div className="painel-sidebar-conta"><BotaoTema className="painel-tema" /><button type="button" className="painel-sair" onClick={aoSair}>Sair <span aria-hidden="true">↗</span></button></div>
    </aside>
    <main id="conteudo-painel" className="painel-conteudo" inert={aberto} tabIndex={-1}>{children}</main>
  </div>;
}
