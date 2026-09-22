"use client";

import { ProvedorAutenticacao } from "@/lib/auth-context";

export default function LayoutPainel({ children }: { children: React.ReactNode }) {
  return <ProvedorAutenticacao>{children}</ProvedorAutenticacao>;
}
