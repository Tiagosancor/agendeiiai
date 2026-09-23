import type { Metadata, Viewport } from "next";
import { Roboto_Slab, IBM_Plex_Sans, Geist_Mono } from "next/font/google";
import { ProvedorTema, scriptTemaInicial } from "@/lib/tema";
import { RegistroServiceWorker } from "@/components/RegistroServiceWorker";
import "./globals.css";

// Tipografia da marca (seção 5.1): Roboto Slab pros títulos e pro nome "Agendeiiai" por
// extenso (dá o ar "carimbado" que combina com a marca); IBM Plex Sans pra interface e
// conteúdo funcional — nunca Geist/Inter puro sem nenhuma escolha própria.
const robotoSlab = Roboto_Slab({
  variable: "--font-roboto-slab",
  subsets: ["latin"],
  weight: ["600", "700"],
});

const plexSans = IBM_Plex_Sans({
  variable: "--font-plex-sans",
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
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
  themeColor: "#1e2a38",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="pt-BR"
      // A classe "dark" é decidida pelo script abaixo, antes da hidratação (seção
      // "tema claro/escuro", Sprint 5) — sem suppressHydrationWarning o React reclamaria
      // da tag <html> ter mudado por fora do próprio ciclo de render dele.
      suppressHydrationWarning
      className={`${robotoSlab.variable} ${plexSans.variable} ${geistMono.variable} h-full antialiased`}
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
