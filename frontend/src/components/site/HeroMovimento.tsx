"use client";

import { useEffect, useRef, type PointerEvent, type ReactNode } from "react";

/** Mantém o HTML do Hero no servidor; hidrata apenas a resposta discreta ao ponteiro. */
export function HeroMovimento({ children }: { children: ReactNode }) {
  const elemento = useRef<HTMLElement>(null);
  const permitido = useRef(false);
  const quadro = useRef(0);
  const posicao = useRef({ x: 0, y: 0 });
  const destino = useRef({ x: 0, y: 0 });

  function atualizar() {
    quadro.current = 0;
    const atual = posicao.current;
    const alvo = destino.current;
    atual.x += (alvo.x - atual.x) * 0.16;
    atual.y += (alvo.y - atual.y) * 0.16;
    elemento.current?.style.setProperty("--hero-cursor-x", `${atual.x.toFixed(2)}px`);
    elemento.current?.style.setProperty("--hero-cursor-y", `${atual.y.toFixed(2)}px`);
    if (Math.abs(alvo.x - atual.x) + Math.abs(alvo.y - atual.y) > 0.04) {
      quadro.current = requestAnimationFrame(atualizar);
    }
  }

  function agendar(x: number, y: number) {
    destino.current = { x, y };
    if (!quadro.current) quadro.current = requestAnimationFrame(atualizar);
  }

  useEffect(() => {
    const hero = elemento.current!;
    const ponteiro = matchMedia("(min-width: 901px) and (hover: hover) and (pointer: fine)");
    const reduzido = matchMedia("(prefers-reduced-motion: reduce)");
    const limpar = () => {
      cancelAnimationFrame(quadro.current);
      quadro.current = 0;
      posicao.current = destino.current = { x: 0, y: 0 };
      hero.style.removeProperty("--hero-cursor-x");
      hero.style.removeProperty("--hero-cursor-y");
    };
    const sincronizar = () => {
      permitido.current = ponteiro.matches && !reduzido.matches;
      hero.dataset.parallax = permitido.current ? "ativo" : "inativo";
      if (!permitido.current) limpar();
    };
    sincronizar();
    ponteiro.addEventListener("change", sincronizar);
    reduzido.addEventListener("change", sincronizar);
    const observador = new IntersectionObserver(([entrada]) => {
      hero.dataset.visivel = String(entrada.isIntersecting);
      if (!entrada.isIntersecting) limpar();
    });
    observador.observe(hero);
    return () => {
      limpar();
      observador.disconnect();
      ponteiro.removeEventListener("change", sincronizar);
      reduzido.removeEventListener("change", sincronizar);
    };
  }, []);

  function mover(evento: PointerEvent<HTMLElement>) {
    if (!permitido.current || evento.pointerType !== "mouse") return;
    const area = evento.currentTarget.getBoundingClientRect();
    const limitar = (valor: number) => Math.max(-1, Math.min(1, valor));
    agendar(limitar((evento.clientX - area.left) / area.width * 2 - 1) * 8,
      limitar((evento.clientY - area.top) / area.height * 2 - 1) * 6);
  }

  return <section ref={elemento} className="site-hero" onPointerMove={mover}
    onPointerLeave={() => { if (permitido.current) agendar(0, 0); }}>{children}</section>;
}
