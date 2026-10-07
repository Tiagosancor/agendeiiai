import type { ReactNode } from "react";
import { BotaoTema } from "@/components/BotaoTema";

export default function LayoutAgendamentos({ children }: { children: ReactNode }) {
  return (
    <>
      <BotaoTema className="fixed right-4 top-4 min-h-11 min-w-11 focus-visible:outline-2 focus-visible:outline-offset-2 motion-reduce:transition-none" />
      {children}
    </>
  );
}
