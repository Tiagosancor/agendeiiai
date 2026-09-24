"use client";

import Script from "next/script";
import { useEffect, useRef, useState } from "react";

declare global {
  interface Window {
    turnstile?: {
      render: (elemento: HTMLElement, opcoes: Record<string, unknown>) => string;
      reset: (id: string) => void;
    };
  }
}

/**
 * Cloudflare Turnstile invisível (seção 8.6.1). Cada token vale uma vez: o pai incrementa
 * `versao` depois de usar um, e o widget gera outro. Só é montado com a chave do site
 * configurada — sem ela, o captcha fica desligado no servidor também.
 */
export function CaptchaTurnstile({
  chaveSite,
  versao,
  aoObterToken,
}: {
  chaveSite: string;
  versao: number;
  aoObterToken: (token: string | null) => void;
}) {
  const container = useRef<HTMLDivElement>(null);
  const idWidget = useRef<string | null>(null);
  const [scriptPronto, setScriptPronto] = useState(false);

  useEffect(() => {
    if (!scriptPronto || !container.current || !window.turnstile || idWidget.current) return;
    idWidget.current = window.turnstile.render(container.current, {
      sitekey: chaveSite,
      appearance: "interaction-only",
      callback: (token: string) => aoObterToken(token),
      "expired-callback": () => aoObterToken(null),
    });
  }, [scriptPronto, chaveSite, aoObterToken]);

  useEffect(() => {
    if (versao > 0 && idWidget.current && window.turnstile) {
      aoObterToken(null);
      window.turnstile.reset(idWidget.current);
    }
  }, [versao, aoObterToken]);

  return (
    <>
      <Script
        src="https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit"
        strategy="afterInteractive"
        onLoad={() => setScriptPronto(true)}
      />
      <div ref={container} />
    </>
  );
}
