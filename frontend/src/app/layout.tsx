import type { Metadata, Viewport } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { ProvedorTema, scriptTemaInicial } from "@/lib/tema";
import { RegistroServiceWorker } from "@/components/RegistroServiceWorker";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Painel",
  description: "Agendamento online para barbearias, salões e clínicas de estética.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: "#2563eb",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="pt-BR"
      // A classe "dark" é decidida pelo script abaixo, antes da hidratação (seção
      // "tema claro/escuro", Sprint 5) — sem suppressHydrationWarning o React reclamaria
      // da tag <html> ter mudado por fora do próprio ciclo de render dele.
      suppressHydrationWarning
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <head>
        {/* Roda antes da pintura, senão a página pisca no tema errado (FOUC). */}
        <script dangerouslySetInnerHTML={{ __html: scriptTemaInicial }} />
      </head>
      <body className="min-h-full flex flex-col">
        <ProvedorTema>{children}</ProvedorTema>
        <RegistroServiceWorker />
      </body>
    </html>
  );
}
