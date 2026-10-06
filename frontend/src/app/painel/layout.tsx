import { ProvedorAutenticacao } from "@/lib/auth-context";
import { IdentidadePainel } from "@/components/painel/IdentidadePainel";
import "@/components/painel/painel-visual.css";

// A marca é configuração de runtime, como na landing.
export const dynamic = "force-dynamic";

export default function LayoutPainel({ children }: { children: React.ReactNode }) {
  return <IdentidadePainel nomeProduto={process.env.MARCA_NOME_PRODUTO ?? "Plataforma"}>
    <div className="painel-visual"><ProvedorAutenticacao>{children}</ProvedorAutenticacao></div>
  </IdentidadePainel>;
}
