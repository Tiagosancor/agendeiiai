"use client";

import { useCallback, useEffect, useState } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { ErroApi, requisicaoApi } from "@/lib/api";
import type { DetalheAssinatura, InstrucoesPagamento, Periodicidade, PlanoPublico } from "@/lib/tipos";
import { ROTULOS_ESTADO_ASSINATURA } from "@/lib/tipos";
import { formatarReais } from "@/lib/formatacao";
import { faixaProfissionais } from "@/components/cadastro/AssistenteCadastro";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeTd, classeTh } from "@/components/estilos";

function formatarData(iso: string | null): string {
  return iso ? new Date(iso).toLocaleDateString("pt-BR") : "—";
}

/** Tela de assinatura (seção 7) — só o Administrador; continua acessível com a assinatura suspensa. */
export default function PaginaAssinatura() {
  const { chamarApi } = useAutenticacao();
  const [detalhe, setDetalhe] = useState<DetalheAssinatura | null>(null);
  const [semAcesso, setSemAcesso] = useState(false);
  const [planos, setPlanos] = useState<PlanoPublico[]>([]);
  const [instrucoes, setInstrucoes] = useState<InstrucoesPagamento | null>(null);
  const [planoEscolhido, setPlanoEscolhido] = useState<string | null>(null);
  const [periodicidade, setPeriodicidade] = useState<Periodicidade>("Mensal");
  const [mensagem, setMensagem] = useState<{ tipo: "erro" | "ok"; texto: string } | null>(null);
  const [salvando, setSalvando] = useState(false);

  const carregar = useCallback(async () => {
    try {
      const dados = await chamarApi<DetalheAssinatura>("/painel/assinatura");
      setDetalhe(dados);
      setPlanoEscolhido(dados.planoId);
      setPeriodicidade(dados.periodicidade);
    } catch (excecao) {
      if (excecao instanceof ErroApi && excecao.status === 403) setSemAcesso(true);
    }
  }, [chamarApi]);

  useEffect(() => {
    // Busca disparada pela montagem da tela.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
    requisicaoApi<PlanoPublico[]>("/cadastro/planos").then(setPlanos).catch(() => setPlanos([]));
  }, [carregar]);

  async function mostrarInstrucoes() {
    setInstrucoes(await chamarApi<InstrucoesPagamento>("/painel/assinatura/instrucoes-pagamento"));
  }

  async function trocarPlano() {
    if (!planoEscolhido) return;
    setSalvando(true);
    setMensagem(null);
    try {
      await chamarApi("/painel/assinatura/plano", { metodo: "POST", corpo: { planoId: planoEscolhido, periodicidade } });
      setMensagem({ tipo: "ok", texto: "Plano atualizado." });
      setInstrucoes(null);
      await carregar();
    } catch (excecao) {
      setMensagem({ tipo: "erro", texto: excecao instanceof ErroApi ? excecao.message : "Não foi possível trocar o plano." });
    } finally {
      setSalvando(false);
    }
  }

  if (semAcesso) {
    return <p className="text-sm text-gray-600 dark:text-neutral-400">Só o administrador do negócio vê e altera a assinatura.</p>;
  }

  if (!detalhe) return <p className="text-sm text-gray-500">Carregando...</p>;

  const prazo =
    detalhe.estado === "EmTeste"
      ? { rotulo: "Teste grátis até", data: detalhe.fimTeste }
      : detalhe.estado === "Atrasada" || detalhe.estado === "Suspensa"
        ? { rotulo: "Carência até", data: detalhe.carenciaAte }
        : { rotulo: "Próximo vencimento", data: detalhe.proximoVencimento };
  const mudouAlgo = planoEscolhido !== detalhe.planoId || periodicidade !== detalhe.periodicidade;

  return (
    <div className="space-y-6">
      <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Assinatura</h1>

      <section className="rounded-xl border-2 border-marca-primaria/20 p-4 dark:border-marca-acento/30">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <p className="font-display text-xl font-bold text-gray-900 dark:text-neutral-50">
              {detalhe.plano} <span className="text-sm font-normal text-gray-500">· {detalhe.periodicidade.toLowerCase()}</span>
            </p>
            <p className="text-sm text-gray-600 dark:text-neutral-400">
              {formatarReais(detalhe.precoMensalTravado)}/mês
              {detalhe.periodicidade === "Anual" && ` · ${formatarReais(detalhe.valorDoPeriodo)} por ano`}
            </p>
          </div>
          <span
            data-testid="estado-assinatura"
            className={`rounded-full px-3 py-1 text-xs font-semibold ${
              detalhe.estado === "Suspensa" || detalhe.estado === "Atrasada"
                ? "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200"
                : "bg-gray-100 text-gray-800 dark:bg-neutral-800 dark:text-neutral-200"
            }`}
          >
            {ROTULOS_ESTADO_ASSINATURA[detalhe.estado]}
          </span>
        </div>

        <dl className="mt-4 grid grid-cols-2 gap-3 text-sm">
          <div>
            <dt className="text-gray-500 dark:text-neutral-400">{prazo.rotulo}</dt>
            <dd className="font-medium">{prazo.data ? formatarData(prazo.data) : "Sem vencimento"}</dd>
          </div>
          <div>
            <dt className="text-gray-500 dark:text-neutral-400">Profissionais ativos</dt>
            <dd className="font-medium">
              {detalhe.profissionaisAtivos} de {detalhe.maximoProfissionais}
            </dd>
          </div>
        </dl>

        <div className="mt-4">
          {!instrucoes ? (
            <button onClick={mostrarInstrucoes} className={`${classeBotaoPrimario} dark:bg-marca-acento dark:text-marca-primaria`}>
              Assinar agora
            </button>
          ) : (
            <div data-testid="instrucoes-pagamento" className="space-y-2 rounded-lg bg-gray-50 p-3 text-sm dark:bg-neutral-900">
              <p>{instrucoes.texto}</p>
              {instrucoes.chavePix && (
                <p className="flex flex-wrap items-center gap-2">
                  Chave PIX: <code className="rounded bg-white px-1.5 py-0.5 dark:bg-neutral-800">{instrucoes.chavePix}</code>
                  <button onClick={() => navigator.clipboard?.writeText(instrucoes.chavePix!)} className="underline">
                    Copiar
                  </button>
                </p>
              )}
              {instrucoes.whatsAppContato && (
                <a
                  href={`https://wa.me/${instrucoes.whatsAppContato}?text=${encodeURIComponent(`Quero assinar o plano ${detalhe.plano}.`)}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-block font-medium underline"
                >
                  Enviar comprovante pelo WhatsApp
                </a>
              )}
              {instrucoes.link && (
                <a href={instrucoes.link} className="inline-block font-medium underline">
                  Pagar agora
                </a>
              )}
            </div>
          )}
        </div>
      </section>

      <section className="space-y-3">
        <h2 className="font-semibold text-gray-900 dark:text-neutral-50">Trocar de plano</h2>
        <div className="inline-flex rounded-lg border border-gray-300 p-1 dark:border-neutral-700">
          {(["Mensal", "Anual"] as Periodicidade[]).map((p) => (
            <button
              key={p}
              onClick={() => setPeriodicidade(p)}
              aria-pressed={periodicidade === p}
              className={`rounded-md px-3 py-1 text-sm ${periodicidade === p ? "bg-marca-primaria text-white dark:bg-marca-acento dark:text-marca-primaria" : "text-gray-700 dark:text-neutral-300"}`}
            >
              {p}
            </button>
          ))}
        </div>
        <div className="grid gap-2 sm:grid-cols-3">
          {planos.map((p) => (
            <label
              key={p.id}
              className={`cursor-pointer rounded-xl border-2 p-3 text-sm ${
                planoEscolhido === p.id ? "border-marca-primaria dark:border-marca-acento" : "border-gray-200 dark:border-neutral-800"
              }`}
            >
              <input type="radio" name="plano" className="sr-only" checked={planoEscolhido === p.id} onChange={() => setPlanoEscolhido(p.id)} />
              <span className="block font-semibold">{p.nome}</span>
              <span className="block text-gray-600 dark:text-neutral-400">{faixaProfissionais(p)}</span>
              <span className="block font-medium">
                {formatarReais(periodicidade === "Anual" ? p.precoAnualPorMes : p.precoMensal)}/mês
              </span>
            </label>
          ))}
        </div>
        {mensagem && (
          <p role="alert" className={mensagem.tipo === "erro" ? "text-sm text-red-700 dark:text-red-400" : "text-sm text-green-700 dark:text-green-400"}>
            {mensagem.texto}
          </p>
        )}
        <button onClick={trocarPlano} disabled={!mudouAlgo || salvando} className={classeBotaoSecundario}>
          {salvando ? "Salvando..." : "Trocar plano"}
        </button>
      </section>

      <section>
        <h2 className="mb-2 font-semibold text-gray-900 dark:text-neutral-50">Histórico de cobranças</h2>
        {detalhe.cobrancas.length === 0 ? (
          <p className="text-sm text-gray-500 dark:text-neutral-400">Nenhuma cobrança registrada ainda.</p>
        ) : (
          <div className={classeCartao}>
            <table className="w-full">
              <thead>
                <tr>
                  <th className={classeTh}>Pago em</th>
                  <th className={classeTh}>Valor</th>
                  <th className={classeTh}>Período</th>
                </tr>
              </thead>
              <tbody>
                {detalhe.cobrancas.map((c) => (
                  <tr key={`${c.pagoEm}-${c.valor}`} className="border-t border-gray-100 dark:border-neutral-800">
                    <td className={classeTd}>{formatarData(c.pagoEm)}</td>
                    <td className={classeTd}>{formatarReais(c.valor)}</td>
                    <td className={classeTd}>
                      {formatarData(c.periodoInicio)} a {formatarData(c.periodoFim)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
