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
      role="status"
      className={
        aviso.destacado
          ? "border-b border-red-200 bg-red-50 text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
          : "border-b border-marca-acento/30 bg-marca-acento/10 text-gray-800 dark:text-neutral-200"
      }
    >
      <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-2 px-4 py-2 text-sm">
        <span>{texto}</span>
        <Link href="/painel/assinatura" className="font-semibold underline">
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
