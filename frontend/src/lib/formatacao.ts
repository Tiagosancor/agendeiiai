import type { PlanoPublico } from "@/lib/tipos";

/** "R$ 49,90" — preços sempre vêm da API (seção 6.4), só a formatação é do front. */
export function formatarReais(valor: number): string {
  return valor.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

/** Sugestão de endereço da página a partir do nome: minúsculas, sem acento, hífens. */
export function sugerirSlug(nome: string): string {
  return nome
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 30)
    .replace(/-+$/g, "");
}

/** Aceita "(71) 99999-9999" (assume Brasil) ou já em E.164 ("+55..."). */
export function paraE164(telefone: string): string {
  const texto = telefone.trim();
  const digitos = texto.replace(/\D/g, "");
  if (texto.startsWith("+")) return `+${digitos}`;
  return digitos.length >= 10 && digitos.length <= 11 ? `+55${digitos}` : `+${digitos}`;
}

/** 0 a 3 — só um indicador visual; a regra mínima de verdade é do servidor. */
export function forcaSenha(senha: string): number {
  if (senha.length < 8 || !/[a-zA-Z]/.test(senha) || !/\d/.test(senha)) return 0;
  let pontos = 1;
  if (senha.length >= 12) pontos++;
  if (/[^a-zA-Z0-9]/.test(senha) || (/[a-z]/.test(senha) && /[A-Z]/.test(senha))) pontos++;
  return pontos;
}

/** "Até 3 profissionais" / "4 a 7 profissionais" — faixa do plano, a partir do banco. */
export function faixaProfissionais(plano: PlanoPublico): string {
  return plano.minimoProfissionais <= 1
    ? `Até ${plano.maximoProfissionais} profissionais`
    : `${plano.minimoProfissionais} a ${plano.maximoProfissionais} profissionais`;
}

/**
 * "2026-09-24" no fuso do NAVEGADOR. Nunca use `toISOString().slice(0, 10)` para isso: ele
 * converte para UTC, e das 21h à meia-noite no Brasil já devolve o dia seguinte.
 */
export function dataLocalIso(data: Date = new Date()): string {
  const mes = String(data.getMonth() + 1).padStart(2, "0");
  const dia = String(data.getDate()).padStart(2, "0");
  return `${data.getFullYear()}-${mes}-${dia}`;
}
