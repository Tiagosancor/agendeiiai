import type { ReactNode } from "react";
import { ProvedorPlataforma } from "@/components/plataforma/ContextoPlataforma";

import { IdentidadePainel } from "@/components/painel/IdentidadePainel";
import "@/components/painel/painel-visual.css";
import "@/components/painel/painel-cadastros.css";
import "@/components/plataforma/plataforma-visual.css";

export const dynamic = "force-dynamic";

export default function LayoutPlataforma({ children }: { children: ReactNode }) {
  return <IdentidadePainel nomeProduto={process.env.MARCA_NOME_PRODUTO ?? "Plataforma"}><div className="painel-visual"><ProvedorPlataforma>{children}</ProvedorPlataforma></div></IdentidadePainel>;
}
