"use client";

import { CampoSenha } from "@/components/CampoSenha";
import { classeInput, classeLabel } from "@/components/estilos";
import type { AcessoProfissional } from "@/lib/tipos";

export interface EstadoAcesso {
  darAcesso: boolean;
  email: string;
  senha: string;
  enviarConvite: boolean;
}

export const ACESSO_INICIAL: EstadoAcesso = { darAcesso: false, email: "", senha: "", enviarConvite: true };

/** O que vai para a API: nulo quando o profissional fica sem login (só um nome na agenda). */
export function acessoParaApi(estado: EstadoAcesso): AcessoProfissional | null {
  if (!estado.darAcesso) return null;
  return { email: estado.email.trim(), senha: estado.enviarConvite ? null : estado.senha, enviarConvite: estado.enviarConvite };
}

/**
 * Acesso do profissional ao sistema (seção 7): "Este profissional vai ter acesso ao sistema?" (padrão: não).
 * Com sim, o e-mail de login e a senha definida agora ou um convite por e-mail para ele criar a própria.
 * Sem <c>perguntar</c>, a resposta já é sim (o botão "Dar acesso ao sistema" da ficha).
 */
export function CamposAcesso({
  valor,
  aoMudar,
  perguntar = true,
}: {
  valor: EstadoAcesso;
  aoMudar: (novo: EstadoAcesso) => void;
  perguntar?: boolean;
}) {
  const mudar = (parcial: Partial<EstadoAcesso>) => aoMudar({ ...valor, ...parcial });
  const mostrarCampos = !perguntar || valor.darAcesso;

  return (
    <fieldset className="space-y-3 rounded-lg border border-gray-200 p-3 dark:border-neutral-800">
      {perguntar && (
        <>
          <legend className="px-1 text-sm font-medium text-gray-700 dark:text-neutral-300">Este profissional vai ter acesso ao sistema?</legend>
          <div className="flex gap-4 text-sm text-gray-700 dark:text-neutral-300">
            <label className="flex items-center gap-2">
              <input type="radio" name="dar-acesso" checked={!valor.darAcesso} onChange={() => mudar({ darAcesso: false })} />
              Não, só aparece na agenda
            </label>
            <label className="flex items-center gap-2">
              <input type="radio" name="dar-acesso" checked={valor.darAcesso} onChange={() => mudar({ darAcesso: true })} />
              Sim
            </label>
          </div>
        </>
      )}

      {mostrarCampos && (
        <>
          <label className="block">
            <span className={classeLabel}>E-mail de acesso</span>
            <input required type="email" className={classeInput} value={valor.email} onChange={(e) => mudar({ email: e.target.value })} />
          </label>
          <div className="space-y-1 text-sm text-gray-700 dark:text-neutral-300">
            <label className="flex items-center gap-2">
              <input type="radio" name="forma-acesso" checked={valor.enviarConvite} onChange={() => mudar({ enviarConvite: true })} />
              Enviar convite por e-mail (ele cria a própria senha)
            </label>
            <label className="flex items-center gap-2">
              <input type="radio" name="forma-acesso" checked={!valor.enviarConvite} onChange={() => mudar({ enviarConvite: false })} />
              Definir a senha agora
            </label>
          </div>
          {!valor.enviarConvite && (
            <div>
              <CampoSenha
                rotulo="Senha de acesso"
                required
                minLength={8}
                autoComplete="new-password"
                className={classeInput}
                value={valor.senha}
                onChange={(e) => mudar({ senha: e.target.value })}
              />
            </div>
          )}
        </>
      )}
    </fieldset>
  );
}
