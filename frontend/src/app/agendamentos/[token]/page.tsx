"use client";

import { use, useEffect, useState } from "react";
import { requisicaoApiPublica, ErroApi } from "@/lib/api";
import type { DetalhePublicoAgendamento } from "@/lib/tipos";

function formatarDataHora(iso: string): string {
  return new Date(iso).toLocaleString("pt-BR", { dateStyle: "full", timeStyle: "short" });
}

export default function PaginaMeuAgendamento({ params }: { params: Promise<{ token: string }> }) {
  const { token } = use(params);

  const [detalhe, setDetalhe] = useState<DetalhePublicoAgendamento | null>(null);
  const [erroCarregar, setErroCarregar] = useState<string | null>(null);
  const [processando, setProcessando] = useState(false);
  const [mensagem, setMensagem] = useState<string | null>(null);
  const [novoInicio, setNovoInicio] = useState("");

  useEffect(() => {
    // Busca disparada pela montagem, a partir do token na URL.
    requisicaoApiPublica<DetalhePublicoAgendamento>(`/meus-agendamentos/${token}`)
      .then(setDetalhe)
      .catch(() => setErroCarregar("Link inválido ou expirado."));
  }, [token]);

  async function cancelar() {
    if (!confirm("Tem certeza que deseja cancelar este agendamento?")) return;

    setProcessando(true);
    setMensagem(null);
    try {
      await requisicaoApiPublica(`/meus-agendamentos/${token}/cancelar`, { metodo: "POST" });
      setDetalhe((atual) => (atual ? { ...atual, status: "Cancelado" } : atual));
      setMensagem("Agendamento cancelado.");
    } catch (excecao) {
      setMensagem(excecao instanceof ErroApi ? excecao.message : "Não foi possível cancelar.");
    } finally {
      setProcessando(false);
    }
  }

  async function remarcar(evento: React.FormEvent) {
    evento.preventDefault();
    if (!novoInicio) return;

    setProcessando(true);
    setMensagem(null);
    try {
      const iso = new Date(novoInicio).toISOString();
      await requisicaoApiPublica(`/meus-agendamentos/${token}/remarcar`, { metodo: "POST", corpo: { novoInicio: iso } });
      const atualizado = await requisicaoApiPublica<DetalhePublicoAgendamento>(`/meus-agendamentos/${token}`);
      setDetalhe(atualizado);
      setMensagem("Agendamento remarcado.");
    } catch (excecao) {
      setMensagem(excecao instanceof ErroApi ? excecao.message : "Não foi possível remarcar. Escolha outro horário.");
    } finally {
      setProcessando(false);
    }
  }

  if (erroCarregar) {
    return (
      <main className="flex min-h-screen items-center justify-center px-6 text-center">
        <p className="text-sm text-gray-500">{erroCarregar}</p>
      </main>
    );
  }

  if (!detalhe) {
    return (
      <main className="flex min-h-screen items-center justify-center px-6 text-center">
        <p className="text-sm text-gray-500">Carregando...</p>
      </main>
    );
  }

  const ativo = detalhe.status === "Agendado";

  return (
    <main className="mx-auto flex min-h-screen max-w-md flex-col justify-center gap-4 px-6 py-12">
      <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">{detalhe.nomeNegocio}</h1>

      <div className="rounded-xl border border-gray-200 p-4 dark:border-neutral-800">
        <p className="text-sm text-gray-500 dark:text-neutral-400">Status: {detalhe.status}</p>
        <p className="mt-1 text-sm text-gray-800 dark:text-neutral-200">{formatarDataHora(detalhe.inicio)}</p>
        <p className="text-sm text-gray-600 dark:text-neutral-400">{detalhe.servicos.join(", ")}</p>
        <p className="text-sm text-gray-600 dark:text-neutral-400">{detalhe.local}</p>
        <p className="mt-2 text-sm font-semibold text-gray-900 dark:text-neutral-50">Total: R$ {detalhe.total.toFixed(2)}</p>
      </div>

      {mensagem && <p className="text-sm text-gray-700 dark:text-neutral-300">{mensagem}</p>}

      {ativo && (
        <>
          <button
            onClick={cancelar}
            disabled={processando}
            className="rounded-lg border border-red-300 px-4 py-2 text-sm font-medium text-red-600 disabled:opacity-60 dark:border-red-900 dark:text-red-400"
          >
            Cancelar agendamento
          </button>

          <form onSubmit={remarcar} className="space-y-2">
            <label className="block text-sm text-gray-600 dark:text-neutral-400">Remarcar para</label>
            <input
              type="datetime-local"
              required
              value={novoInicio}
              onChange={(e) => setNovoInicio(e.target.value)}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-neutral-700 dark:bg-neutral-900"
            />
            <button
              type="submit"
              disabled={processando}
              className="w-full rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-60"
            >
              Remarcar
            </button>
          </form>
        </>
      )}
    </main>
  );
}
