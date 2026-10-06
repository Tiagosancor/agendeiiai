"use client";

import { createContext, useContext, type ReactNode } from "react";
import Image from "next/image";

const ContextoMarca = createContext("Plataforma");

export function IdentidadePainel({ nomeProduto, children }: { nomeProduto: string; children: ReactNode }) {
  // Mesmo alias visual da landing, preservando a configuração da marca white-label.
  const nomeVisual = nomeProduto.toLowerCase() === "agendeiiai" ? "agendei ai" : nomeProduto;
  return <ContextoMarca.Provider value={nomeVisual}>{children}</ContextoMarca.Provider>;
}

export function MarcaPainel({ tagline = false }: { tagline?: boolean }) {
  const nome = useContext(ContextoMarca);
  return <span className="painel-marca">
    <Image src="/brand/calendario-confirmado.svg" alt="" width={34} height={34} />
    <span><strong>{nome}</strong>{tagline && <small>Agendou, tá confirmado!</small>}</span>
  </span>;
}
