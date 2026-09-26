"use client";

import { classeInput, classeLabel } from "@/components/estilos";
import type { Endereco } from "@/lib/tipos";

/** Endereço opcional da ficha de usuário/profissional (seção 7). */
export function CamposEndereco({ valor, aoMudar }: { valor: Endereco; aoMudar: (endereco: Endereco) => void }) {
  const campo = (chave: keyof Endereco, rotulo: string, classe = "") => (
    <label className={classe}>
      <span className={classeLabel}>{rotulo}</span>
      <input className={classeInput} value={valor[chave] ?? ""} onChange={(e) => aoMudar({ ...valor, [chave]: e.target.value || null })} />
    </label>
  );

  return (
    <fieldset className="grid grid-cols-2 gap-2">
      <legend className="mb-1 text-sm font-medium text-gray-700 dark:text-neutral-300">Endereço (opcional)</legend>
      {campo("rua", "Rua", "col-span-2")}
      {campo("numero", "Número")}
      {campo("cep", "CEP")}
      {campo("bairro", "Bairro")}
      {campo("cidade", "Cidade")}
    </fieldset>
  );
}
