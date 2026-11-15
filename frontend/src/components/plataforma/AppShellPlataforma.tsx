"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { BotaoTema } from "@/components/BotaoTema";
import { MarcaPainel } from "@/components/painel/IdentidadePainel";

// Navegação administrativa; a API preserva a autorização da plataforma.
type ItemMenu = readonly [href: string, rotulo: string, desenho: string];

const ITENS_MENU: readonly ItemMenu[] = [
  ["/plataforma", "Negócios", "M3 9l2-6h14l2 6M3 9v4h18V9M5 13v8h14v-8M9 21v-6h6v6"],
  ["/plataforma/notificacoes", "Notificações", "M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9M10 21a2 2 0 0 0 4 0"],
];

export function AppShellPlataforma({ children, aoSair }: { children: ReactNode; aoSair: () => void }) {
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
    <a className="painel-pular" href="#conteudo-plataforma" inert={aberto}>Ir para o conteúdo</a>
    <header className="painel-header-mobile" inert={aberto}>
      <button ref={botaoMenu} type="button" className="painel-menu-controle" aria-label="Abrir menu" aria-expanded={aberto} aria-controls="navegacao-plataforma" onClick={() => setAberto(true)}>
        <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true"><path d="M4 6h16M4 12h16M4 18h16" /></svg>
      </button>
      <MarcaPainel />
    </header>
    {aberto && <div className="painel-menu-fundo" onClick={() => setAberto(false)} aria-hidden="true" />}
    <aside ref={sidebar} id="navegacao-plataforma" className={`painel-sidebar ${aberto ? "painel-sidebar-aberta" : ""}`} role={aberto ? "dialog" : undefined} aria-modal={aberto ? true : undefined} aria-label="Menu principal">
      <div className="painel-sidebar-marca"><MarcaPainel /><button type="button" className="painel-menu-controle painel-menu-fechar" aria-label="Fechar menu" onClick={() => setAberto(false)}>✕</button></div>
      <nav aria-label="Navegação da plataforma"><ul>{ITENS_MENU.map(([href, rotulo, desenho]) => {
        const ativo = caminho === href || (href !== "/plataforma" && caminho.startsWith(`${href}/`));
        return <li key={href}><Link href={href} aria-current={ativo ? "page" : undefined} onClick={() => setAberto(false)}>
          <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={desenho} /></svg><span>{rotulo}</span>
        </Link></li>;
      })}</ul></nav>
      <div className="painel-sidebar-conta"><BotaoTema className="painel-tema" /><button type="button" className="painel-sair" onClick={aoSair}>Sair <span aria-hidden="true">↗</span></button></div>
    </aside>
    <main id="conteudo-plataforma" className="painel-conteudo" inert={aberto} tabIndex={-1}>{children}</main>
  </div>;
}
