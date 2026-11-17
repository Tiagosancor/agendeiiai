"use client";

import { CamposIdentificadores } from "./CamposIdentificadores";
import { ErroApi } from "@/lib/api";
import { validarIdentificadores } from "@/lib/acesso";
import { CampoSenha } from "@/components/CampoSenha";
import { classeInput } from "@/components/estilos";
import type { AcessoProfissional } from "@/lib/tipos";

export interface EstadoAcesso {
  darAcesso: boolean;
  email: string;
  nomeUsuario: string;
  telefoneLogin: string;
  senha: string;
  enviarConvite: boolean;
}

export const ACESSO_INICIAL: EstadoAcesso = { darAcesso: false, email: "", nomeUsuario: "", telefoneLogin: "", senha: "", enviarConvite: true };

/** O que vai para a API: nulo quando o profissional fica sem login (só um nome na agenda). */
export function acessoParaApi(estado: EstadoAcesso): AcessoProfissional | null {
  if (!estado.darAcesso) return null;
  const erro = validarIdentificadores(estado);
  if (erro) throw new ErroApi(400, erro, "identificador_obrigatorio");
  const enviarConvite = estado.enviarConvite && !!estado.email.trim();
  return { email: estado.email.trim() || null, nomeUsuario: estado.nomeUsuario.trim() || null, telefoneLogin: estado.telefoneLogin.trim() || null, senha: enviarConvite ? null : estado.senha, enviarConvite };
}

/**
 * Acesso do profissional ao sistema (seção 7): "Este profissional vai ter acesso ao sistema?" (padrão: não).
 * Com sim, os identificadores de login e a senha definida agora ou um convite por e-mail.
 * Sem <c>perguntar</c>, a resposta já é sim (o botão "Dar acesso ao sistema" da ficha).
 */
export function CamposAcesso({
  valor,
  aoMudar,
  perguntar = true,
  telefoneContato,
  erro,
}: {
  valor: EstadoAcesso;
  aoMudar: (novo: EstadoAcesso) => void;
  perguntar?: boolean;
  telefoneContato?: string;
  erro?: unknown;
}) {
  const mudar = (parcial: Partial<EstadoAcesso>) => aoMudar({ ...valor, ...parcial });
  const convite = valor.enviarConvite && !!valor.email.trim();
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
          <CamposIdentificadores valor={valor} aoMudar={(campos) => mudar(campos)} telefoneContato={telefoneContato} erro={erro} />
          <div className="space-y-1 text-sm text-gray-700 dark:text-neutral-300">
            <label className="flex items-center gap-2">
              <input type="radio" name="forma-acesso" disabled={!valor.email.trim()} checked={convite} onChange={() => mudar({ enviarConvite: true })} />
              Enviar convite por e-mail (ele cria a própria senha)
            </label>
            <label className="flex items-center gap-2">
              <input type="radio" name="forma-acesso" checked={!convite} onChange={() => mudar({ enviarConvite: false })} />
              Definir a senha agora
            </label>
          </div>
          {!convite && (
            <div>
              <p className="text-xs text-gray-500 dark:text-neutral-400">Pelo menos 8 caracteres, incluindo letra e número.</p>
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
