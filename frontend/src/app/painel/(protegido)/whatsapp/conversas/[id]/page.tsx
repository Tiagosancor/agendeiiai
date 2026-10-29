import { ConversasWhatsApp } from "@/components/painel/ConversasWhatsApp";

export default async function PaginaConversa({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <ConversasWhatsApp id={id} />;
}
