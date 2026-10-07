import type { ReactNode } from "react";
import { BotaoTema } from "@/components/BotaoTema";
import Image from "next/image";
import "@/components/publico/experiencia-publica.css";
import "@/components/publico/gestao-agendamento.css";

export const dynamic = "force-dynamic";

export default function LayoutAgendamentos({ children }: { children: ReactNode }) {
  const nome = process.env.MARCA_NOME_PRODUTO ?? "Plataforma";
  const marca = nome.toLowerCase() === "agendeiiai" ? "agendei ai" : nome;
  return (
    <div className="pagina-publica gestao-publica" data-fundo-personalizado="false">
      <header className="gestao-cabecalho">
        <div className="gestao-identidade"><Image src="/brand/calendario-confirmado.svg" width={30} height={30} alt="" /><span>Seu agendamento</span></div>
        <BotaoTema className="gestao-tema min-h-11 min-w-11 motion-reduce:transition-none" />
      </header>
      {children}
      <footer className="gestao-rodape">Agendamento organizado com <strong>{marca}</strong></footer>
    </div>
  );
}
