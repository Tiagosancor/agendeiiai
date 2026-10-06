"use client";

import { classeBotaoSecundario, classeCartao } from "@/components/estilos";
import type { AgendaSemana } from "@/lib/tipos";
import { dataLocalIso } from "@/lib/formatacao";

const NOMES_DIA = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];

const formatarHora = (iso: string) => new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });

const COR_STATUS: Record<string, string> = {
  Agendado: "border-l-blue-500",
  EmAtendimento: "border-l-violet-500",
  Concluido: "border-l-green-500",
  Cancelado: "border-l-gray-300 line-through opacity-60",
  Faltou: "border-l-red-500 opacity-70",
  Reservado: "border-l-yellow-500",
};

/** "2026-09-29" → a segunda-feira da semana desse dia, em "yyyy-mm-dd" (no fuso do navegador, como o resto da agenda). */
export function inicioDaSemana(dataIso: string): string {
  const [ano, mes, dia] = dataIso.split("-").map(Number);
  const data = new Date(ano, mes - 1, dia);
  const recuo = (data.getDay() + 6) % 7; // segunda = 0
  data.setDate(data.getDate() - recuo);
  return dataLocalIso(data);
}

export function somarDias(dataIso: string, dias: number): string {
  const [ano, mes, dia] = dataIso.split("-").map(Number);
  return dataLocalIso(new Date(ano, mes - 1, dia + dias));
}

/**
 * Visão "Semana" (seção 7): um profissional, os 7 dias em colunas (empilhados no celular), com expediente, folga,
 * bloqueios e os atendimentos. Clicar num dia abre a lista daquele dia, onde ficam as ações.
 */
export function SemanaAgenda({
  semana,
  aoMudarSemana,
  aoEscolherDia,
}: {
  semana: AgendaSemana | null;
  aoMudarSemana: (deslocamentoDias: number) => void;
  aoEscolherDia: (data: string) => void;
}) {
  const hoje = dataLocalIso();

  return (
    <div className="painel-semana space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <button type="button" className={classeBotaoSecundario} onClick={() => aoMudarSemana(-7)}>
          ← Semana anterior
        </button>
        {semana && (
          <span className="text-sm font-medium text-gray-900 dark:text-neutral-50">
            {semana.nome} · {formatarDia(semana.dias[0].data)} a {formatarDia(semana.dias[6].data)}
          </span>
        )}
        <button type="button" className={classeBotaoSecundario} onClick={() => aoMudarSemana(7)}>
          Próxima semana →
        </button>
      </div>

      {!semana ? (
        <p className="text-sm text-gray-500 dark:text-neutral-400">Carregando...</p>
      ) : (
        <div className="grid gap-2 md:grid-cols-7" role="list" aria-label="Semana">
          {semana.dias.map((dia) => {
            const [ano, mes, d] = dia.data.split("-").map(Number);
            const nome = NOMES_DIA[new Date(ano, mes - 1, d).getDay()];
            return (
              <section
                key={dia.data}
                role="listitem"
                aria-label={`${nome} ${formatarDia(dia.data)}`}
                className={`${classeCartao} flex min-h-32 flex-col p-2 ${dia.data === hoje ? "ring-2 ring-marca-primaria dark:ring-marca-acento" : ""}`}
              >
                <button
                  type="button"
                  className="mb-1 text-left text-sm font-semibold text-gray-900 hover:underline dark:text-neutral-50"
                  onClick={() => aoEscolherDia(dia.data)}
                >
                  {nome} {formatarDia(dia.data)}
                </button>
                <p className="text-xs text-gray-500 dark:text-neutral-400">{dia.folga ? "Folga" : dia.turnos.join(" · ")}</p>
                {dia.bloqueios.map((b) => (
                  <p key={b} className="text-xs text-amber-700 dark:text-amber-400">
                    {b}
                  </p>
                ))}
                <ul className="mt-2 space-y-1">
                  {dia.agendamentos.map((a) => (
                    <li
                      key={a.id}
                      className={`rounded border-l-4 bg-gray-50 px-2 py-1 text-xs text-gray-800 dark:bg-neutral-800 dark:text-neutral-200 ${COR_STATUS[a.status] ?? ""}`}
                    >
                      <span className="font-medium">{formatarHora(a.inicio)}</span> {a.clienteNome}
                      {a.forcado && <span className="ml-1 text-amber-700 dark:text-amber-400">(forçado)</span>}
                      <span className="block truncate text-gray-500 dark:text-neutral-400">{a.servicos.join(", ")}</span>
                    </li>
                  ))}
                </ul>
                {dia.agendamentos.length === 0 && !dia.folga && <p className="mt-2 text-xs text-gray-400 dark:text-neutral-500">Livre</p>}
              </section>
            );
          })}
        </div>
      )}
    </div>
  );
}

function formatarDia(iso: string): string {
  const [, mes, dia] = iso.split("-");
  return `${dia}/${mes}`;
}
