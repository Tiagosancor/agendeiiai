import type { ReactNode } from "react";
import { BotaoTema } from "@/components/BotaoTema";

export default function LayoutAgendamentos({ children }: { children: ReactNode }) {
  return (
    <>
      <BotaoTema className="fixed right-4 top-4" />
      {children}
    </>
  );
}
