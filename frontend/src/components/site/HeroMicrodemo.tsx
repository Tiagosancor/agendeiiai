"use client";

import { useEffect, useRef, useState } from "react";

const ETAPAS = ["Escolher serviços", "Escolher profissional, data e horário", "Revisar e confirmar"];

/** Demonstra somente as capturas disponíveis; o resumo ainda precede a confirmação real. */
export function HeroMicrodemo() {
  const elemento = useRef<HTMLDivElement>(null);
  const iniciado = useRef(false);
  const [etapa, definirEtapa] = useState(0);
  const [manual, definirManual] = useState(false);
  const [disponivel, definirDisponivel] = useState(false);

  useEffect(() => {
    const reduzido = matchMedia("(prefers-reduced-motion: reduce)");
    let naTela = false;
    const sincronizar = () => definirDisponivel(naTela && !document.hidden && !reduzido.matches);
    const observador = new IntersectionObserver(([entrada]) => {
      naTela = entrada.isIntersecting;
      sincronizar();
    });
    observador.observe(elemento.current!.closest("section")!);
    document.addEventListener("visibilitychange", sincronizar);
    reduzido.addEventListener("change", sincronizar);
    return () => {
      observador.disconnect();
      document.removeEventListener("visibilitychange", sincronizar);
      reduzido.removeEventListener("change", sincronizar);
    };
  }, []);

  useEffect(() => {
    if (!disponivel || manual) return;
    // A entrada termina em .82s; aguarda mais 1.5s antes do início automático.
    const espera = !iniciado.current ? 2300 : etapa === 2 ? 4200 : 3400;
    const timer = window.setTimeout(() => {
      iniciado.current = true;
      definirEtapa(atual => (atual + 1) % ETAPAS.length);
    }, espera);
    return () => window.clearTimeout(timer);
  }, [disponivel, manual, etapa]);

  return <div ref={elemento} className="site-hero-celulares site-microdemo" data-etapa={etapa} data-autoplay={disponivel && !manual ? "ativo" : "pausado"}>
    <div className="site-celular site-celular-frente" data-ativo={etapa !== 0}>
      <div className="site-demo-telas">
        <img src="/site/assistente-horarios.webp" alt="Assistente de agendamento no celular: escolha de data e horário" width={390} height={780} loading="eager" fetchPriority="high" decoding="sync" aria-hidden={etapa === 2} className={etapa === 2 ? "site-demo-tela-inativa" : ""} />
        <img src="/site/assistente-resumo.webp" alt="Resumo real antes de confirmar o agendamento" width={390} height={780} loading="eager" decoding="async" aria-hidden={etapa !== 2} className={`site-demo-resumo ${etapa !== 2 ? "site-demo-tela-inativa" : ""}`} />
      </div>
    </div>
    <div className="site-celular site-celular-atras" data-ativo={etapa === 0}>
      <img src="/site/assistente-servicos.webp" alt="Escolha os serviços no assistente de agendamento real" width={390} height={780} loading="eager" decoding="async" />
    </div>
    <div className="site-demo-controles" role="group" aria-label="Demonstração do agendamento">
      {ETAPAS.map((rotulo, indice) => <button key={rotulo} type="button" aria-label={`Ver etapa: ${rotulo.toLowerCase()}`} aria-pressed={etapa === indice} onClick={() => { definirManual(true); definirEtapa(indice); }}><span aria-hidden="true" /></button>)}
      <button type="button" className="site-demo-pausa" aria-label={manual ? "Retomar demonstração automática" : "Pausar demonstração automática"} onClick={() => definirManual(atual => !atual)}><span aria-hidden="true">{manual ? "▶" : "Ⅱ"}</span></button>
      <span className="site-demo-legenda">{ETAPAS[etapa]}</span>
    </div>
  </div>;
}
