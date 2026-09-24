import type { Metadata } from "next";
import { AdministracaoPlataforma } from "@/components/plataforma/AdministracaoPlataforma";

export const metadata: Metadata = { title: "Administração", robots: { index: false, follow: false } };

export default function PaginaPlataforma() {
  return <AdministracaoPlataforma />;
}
