import type { ReactNode } from "react";
import { ProvedorPlataforma } from "@/components/plataforma/ContextoPlataforma";

export default function LayoutPlataforma({ children }: { children: ReactNode }) {
  return <ProvedorPlataforma>{children}</ProvedorPlataforma>;
}
