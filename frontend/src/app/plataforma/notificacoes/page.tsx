import type { Metadata } from "next";
import { MonitoramentoOutbox } from "@/components/plataforma/MonitoramentoOutbox";

export const metadata: Metadata = { title: "Notificações | Administração", robots: { index: false, follow: false } };

export default function PaginaNotificacoesPlataforma() {
  return <MonitoramentoOutbox />;
}
