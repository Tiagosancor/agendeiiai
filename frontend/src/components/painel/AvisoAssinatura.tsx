"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import type { AvisoAssinatura as Aviso } from "@/lib/tipos";

/**
 * Aviso permanente do topo do painel (seção 7): discreto nos últimos 7 dias antes do fim do
 * teste ou do vencimento, destacado em carência ou suspensão. Quem decide é o servidor.
 */
export function AvisoAssinatura() {
  const { chamarApi } = useAutenticacao();
  const [aviso, setAviso] = useState<Aviso | null>(null);

  useEffect(() => {
    chamarApi<Aviso | undefined>("/painel/assinatura/aviso")
      .then((resposta) => setAviso(resposta ?? null))
      .catch(() => setAviso(null));
  }, [chamarApi]);

  if (!aviso) return null;

  const texto = textoDoAviso(aviso);

  return (
    <div
      data-testid="aviso-assinatura"
      data-estado={aviso.estado}
      data-destacado={aviso.destacado}
      role="status"
      className="painel-aviso-assinatura"
    >
      <div className="painel-aviso-assinatura-conteudo">
        <span className="painel-aviso-assinatura-icone" aria-hidden="true">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
            <circle cx="12" cy="12" r="8.5" />
            <path d="M12 7.5v5l3 1.8" />
          </svg>
        </span>
        <span className="painel-aviso-assinatura-mensagem">{texto}</span>
        <Link href="/painel/assinatura" className="painel-aviso-assinatura-acao">
          Assinar agora
        </Link>
      </div>
    </div>
  );
}

function textoDoAviso(aviso: Aviso): string {
  const quando = aviso.diasRestantes === 1 ? "amanhã" : `em ${aviso.diasRestantes} dias`;

  switch (aviso.estado) {
    case "EmTeste":
      return `Seu teste grátis termina ${quando}.`;
    case "Ativa":
      return `Sua assinatura vence ${quando}.`;
    case "Atrasada":
      return "O pagamento da assinatura está pendente. Regularize para não interromper os agendamentos online.";
    case "Suspensa":
      return "Sua assinatura está suspensa. Os agendamentos online estão pausados até o pagamento.";
    default:
      return "Confira sua assinatura.";
  }
}
