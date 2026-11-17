"use client";

import { useId } from "react";
import { classeInput, classeLabel } from "@/components/estilos";
import { ErroApi } from "@/lib/api";
import type { IdentificadoresAcesso } from "@/lib/acesso";

export function CamposIdentificadores({ valor, aoMudar, telefoneContato, erro }: {
  valor: IdentificadoresAcesso;
  aoMudar: (valor: IdentificadoresAcesso) => void;
  telefoneContato?: string;
  erro?: unknown;
}) {
  const id = useId();
  const conflito = erro instanceof ErroApi && erro.codigo === "identificador_indisponivel" ? erro : null;
  const sugestao = typeof conflito?.corpo?.sugestao === "string" ? conflito.corpo.sugestao : null;
  return <div className="space-y-3">
    <p className="text-sm text-gray-600 dark:text-neutral-300">Informe pelo menos um identificador de acesso.</p>
    {([
      ["email", "E-mail de acesso (opcional)", "email"],
      ["nomeUsuario", "Nome de usuário (opcional)", "text"],
      ["telefoneLogin", "Telefone de login (opcional)", "tel"],
    ] as const).map(([campo, rotulo, tipo]) => <div key={campo}>
      <label htmlFor={`${id}-${campo}`} className={classeLabel}>{rotulo}</label>
      <input id={`${id}-${campo}`} type={tipo} className={classeInput} value={valor[campo]}
        autoCapitalize="none" spellCheck={false} autoComplete="off"
        aria-invalid={conflito?.corpo?.campo === campo || undefined}
        aria-describedby={conflito?.corpo?.campo === campo ? `${id}-${campo}-erro` : campo === "nomeUsuario" ? `${id}-dica` : undefined}
        onChange={(e) => aoMudar({ ...valor, [campo]: e.target.value })} />
      {campo === "nomeUsuario" && <p id={`${id}-dica`} className="text-xs text-gray-500 dark:text-neutral-400">De 3 a 30 caracteres, com letra. Use letras sem acento, números, ponto, hífen ou sublinhado; sem sinais seguidos ou no final.</p>}
      {conflito?.corpo?.campo === campo && <div id={`${id}-${campo}-erro`} role="alert" className="text-sm text-red-600 dark:text-red-400">
        {conflito.message}
        {campo === "nomeUsuario" && sugestao && <button type="button" className="ml-2 underline" onClick={() => aoMudar({ ...valor, nomeUsuario: sugestao })}>Usar {sugestao}</button>}
      </div>}
    </div>)}
    {telefoneContato?.trim() && <button type="button" className="text-sm underline" onClick={() => aoMudar({ ...valor, telefoneLogin: telefoneContato })}>Usar o telefone do cadastro para entrar</button>}
  </div>;
}
