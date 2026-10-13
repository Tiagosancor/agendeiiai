"use client";

import { useState } from "react";
import { ErroApi } from "./api";

export const CABECALHO_IDEMPOTENCIA = "Idempotency-Key";

type Tentativa = { chave: string; fingerprint: string };

function ordenar(valor: unknown): unknown {
  if (Array.isArray(valor)) return valor.map(ordenar);
  if (valor && typeof valor === "object") {
    return Object.fromEntries(
      Object.entries(valor as Record<string, unknown>)
        .sort(([a], [b]) => a.localeCompare(b))
        .map(([chave, item]) => [chave, ordenar(item)]),
    );
  }
  return valor;
}

export function fingerprintDaIntencao(payload: unknown): string {
  return JSON.stringify(ordenar(payload));
}

/** Mantém a chave somente em memória durante uma intenção do usuário. */
export class TentativaIdempotente {
  private tentativa: Tentativa | null = null;
  private emAndamento: { fingerprint: string; promessa: Promise<unknown> } | null = null;

  executar<T>(payload: unknown, enviar: (chave: string) => Promise<T>): Promise<T> {
    const fingerprint = fingerprintDaIntencao(payload);
    if (this.emAndamento?.fingerprint === fingerprint) return this.emAndamento.promessa as Promise<T>;

    if (!this.tentativa || this.tentativa.fingerprint !== fingerprint) {
      this.tentativa = { chave: crypto.randomUUID(), fingerprint };
    }

    const tentativaAtual = this.tentativa;
    const promessa = enviar(tentativaAtual.chave)
      .then((resultado) => {
        if (this.tentativa?.chave === tentativaAtual.chave) this.tentativa = null;
        return resultado;
      })
      .catch((erro) => {
        if (erro instanceof ErroApi && erro.status === 422 && erro.codigo === "idempotency_key_reutilizada") {
          if (this.tentativa?.chave === tentativaAtual.chave) this.tentativa = null;
        }
        throw erro;
      })
      .finally(() => {
        if (this.emAndamento?.promessa === promessa) this.emAndamento = null;
      });

    this.emAndamento = { fingerprint, promessa };
    return promessa;
  }

  descartar(): void {
    this.tentativa = null;
    this.emAndamento = null;
  }
}

export function useTentativaIdempotente(): TentativaIdempotente {
  const [tentativa] = useState(() => new TentativaIdempotente());
  return tentativa;
}

export function cabecalhoIdempotencia(chave: string): Record<string, string> {
  return { [CABECALHO_IDEMPOTENCIA]: chave };
}

export function mensagemErroIdempotencia(erro: unknown, mensagemPadrao: string): string {
  if (erro instanceof ErroApi && erro.codigo === "operacao_em_andamento") {
    const espera = erro.retryAfterSegundos ? ` Aguarde ${erro.retryAfterSegundos} segundos` : " Aguarde alguns instantes";
    return `A operação ainda está sendo processada.${espera} e tente novamente.`;
  }
  if (erro instanceof ErroApi && erro.codigo === "idempotency_key_reutilizada") {
    return "Os dados desta tentativa mudaram. Confira as informações e tente novamente.";
  }
  return erro instanceof ErroApi ? erro.message : mensagemPadrao;
}
