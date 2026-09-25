"use client";

import Link from "next/link";
import { useState } from "react";
import type { Periodicidade, PlanoPublico } from "@/lib/tipos";
import { faixaProfissionais, formatarReais } from "@/lib/formatacao";

/**
 * Cards de planos do site (seção 6.4). Preços e destaque vêm do banco (`/cadastro/planos`);
 * a economia do anual também é calculada dos preços, nunca escrita no texto — se um dia as
 * faixas tiverem diferenças distintas, o rótulo vira "até R$ X".
 */
export function PlanosSite({ planos, whatsApp }: { planos: PlanoPublico[]; whatsApp: string | null }) {
  const [periodicidade, setPeriodicidade] = useState<Periodicidade>("Mensal");

  const economias = planos.map((p) => Math.round((p.precoMensal - p.precoAnualPorMes) * 12 * 100) / 100);
  const maiorEconomia = Math.max(0, ...economias);
  const economiaIgual = economias.every((e) => e === economias[0]);

  if (planos.length === 0) {
    return (
      <p className="text-center text-sm text-gray-600 dark:text-neutral-400">
        Não foi possível carregar os planos agora.{" "}
        <Link href="/cadastro" className="font-medium underline">
          Comece o teste grátis
        </Link>{" "}
        e escolha o plano no cadastro.
      </p>
    );
  }

  return (
    <div>
      <div className="flex flex-col items-center gap-2">
        <div role="radiogroup" aria-label="Periodicidade" className="inline-flex rounded-xl border border-gray-300 bg-white p-1 dark:border-white/15 dark:bg-white/5">
          {(["Mensal", "Anual"] as Periodicidade[]).map((p) => (
            <button
              key={p}
              type="button"
              role="radio"
              aria-checked={periodicidade === p}
              onClick={() => setPeriodicidade(p)}
              className={`rounded-lg px-5 py-2 text-sm font-medium transition ${
                periodicidade === p
                  ? "bg-marca-primaria text-white dark:bg-marca-acento dark:text-marca-primaria"
                  : "text-gray-700 hover:text-gray-900 dark:text-neutral-300 dark:hover:text-neutral-50"
              }`}
            >
              {p}
            </button>
          ))}
        </div>
        {maiorEconomia > 0 && (
          <p className="text-sm font-medium text-marca-madeira dark:text-marca-acento">
            No anual, {economiaIgual ? "economize" : "economize até"} {formatarReais(maiorEconomia).replace(/,00$/, "")} por ano
          </p>
        )}
      </div>

      <div className="mt-8 grid gap-4 md:grid-cols-3 md:items-stretch">
        {planos.map((p) => {
          const porMes = periodicidade === "Anual" ? p.precoAnualPorMes : p.precoMensal;
          return (
            <div
              key={p.id}
              data-testid="card-plano"
              className={`relative flex flex-col rounded-2xl p-6 ${
                p.destaque
                  ? "bg-marca-primaria text-white shadow-xl ring-2 ring-marca-acento md:-my-3 md:py-9"
                  : "border border-gray-200 bg-white dark:border-white/10 dark:bg-white/5"
              }`}
            >
              {p.destaque && (
                <span className="absolute -top-3 left-6 rounded-full bg-marca-acento px-3 py-1 text-xs font-semibold text-marca-primaria">
                  Mais escolhido
                </span>
              )}
              <h3 className="font-display text-xl font-bold">{p.nome}</h3>
              <p className={`mt-1 text-sm ${p.destaque ? "text-white/80" : "text-gray-600 dark:text-neutral-400"}`}>{faixaProfissionais(p)}</p>

              <p className="mt-5">
                <span className="font-display text-4xl font-bold">{formatarReais(porMes)}</span>
                <span className={`text-sm ${p.destaque ? "text-white/80" : "text-gray-600 dark:text-neutral-400"}`}>/mês</span>
              </p>
              <p className={`mt-1 min-h-5 text-xs ${p.destaque ? "text-white/80" : "text-gray-600 dark:text-neutral-400"}`}>
                {periodicidade === "Anual" ? `${formatarReais(p.precoAnualPorMes * 12)} cobrados por ano` : "cobrado todo mês"}
              </p>

              <Link
                href={`/cadastro?plano=${p.id}&periodicidade=${periodicidade}`}
                className={`mt-6 rounded-xl px-4 py-3 text-center text-sm font-semibold transition ${
                  p.destaque
                    ? "bg-marca-acento text-marca-primaria hover:brightness-110"
                    : "bg-marca-primaria text-white hover:bg-marca-primaria-hover dark:bg-neutral-100 dark:text-marca-primaria dark:hover:bg-white"
                }`}
              >
                Começar teste grátis
              </Link>
            </div>
          );
        })}
      </div>

      <p className="mt-8 text-center text-sm text-gray-700 dark:text-neutral-300">
        Mais de {Math.max(...planos.map((p) => p.maximoProfissionais))} profissionais?{" "}
        {whatsApp ? (
          <a
            href={`https://wa.me/${whatsApp}?text=${encodeURIComponent("Olá! Tenho mais profissionais do que o maior plano. Podemos conversar?")}`}
            target="_blank"
            rel="noopener noreferrer"
            className="font-semibold underline decoration-marca-acento decoration-2 underline-offset-4"
          >
            Fale com a gente
          </a>
        ) : (
          <a href="#contato" className="font-semibold underline decoration-marca-acento decoration-2 underline-offset-4">
            Fale com a gente
          </a>
        )}
      </p>
    </div>
  );
}
