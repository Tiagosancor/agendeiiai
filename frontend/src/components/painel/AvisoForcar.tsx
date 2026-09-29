"use client";

import { useEffect, useState } from "react";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { classeBotaoPrimario, classeBotaoSecundario, classeInput, classeLabel } from "@/components/estilos";
import type { ConsultaForcar, PreviaForcar } from "@/lib/tipos";

/**
 * Forçar agendamento (seção 7): depois que o sistema recusou o horário, quem tem a permissão vê exatamente
 * quais regras serão quebradas e pode "Forçar mesmo assim", com motivo obrigatório. O que nunca pode ser
 * forçado (profissional inativo, horário que já passou) aparece como impedimento, sem o botão.
 */
export function AvisoForcar({
  consulta,
  aoForcar,
  aoDesistir,
}: {
  consulta: ConsultaForcar;
  aoForcar: (motivo: string) => Promise<void>;
  aoDesistir: () => void;
}) {
  const { chamarApi } = useAutenticacao();
  const [previa, setPrevia] = useState<PreviaForcar | null>(null);
  const [motivo, setMotivo] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const chaveConsulta = JSON.stringify(consulta);

  useEffect(() => {
    let atual = true;
    chamarApi<PreviaForcar>("/painel/agendamentos/forcados/previa", { metodo: "POST", corpo: JSON.parse(chaveConsulta) })
      .then((resultado) => atual && setPrevia(resultado))
      .catch(() => atual && setErro("Não foi possível conferir as regras de horário."));
    return () => {
      atual = false;
    };
  }, [chamarApi, chaveConsulta]);

  async function forcar() {
    setErro(null);
    setEnviando(true);
    try {
      await aoForcar(motivo.trim());
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível forçar o agendamento.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div role="region" aria-label="Forçar agendamento" className="space-y-3 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm dark:border-amber-700 dark:bg-amber-950/40">
      {!previa && !erro && <p className="text-gray-600 dark:text-neutral-400">Conferindo as regras de horário...</p>}

      {previa?.impedimento && <p className="font-medium text-red-700 dark:text-red-400">Não dá para forçar: {previa.impedimento}</p>}

      {previa && !previa.impedimento && previa.regras.length === 0 && (
        <p className="text-gray-700 dark:text-neutral-300">Nenhuma regra de horário seria quebrada — tente de novo pelo caminho normal.</p>
      )}

      {previa && !previa.impedimento && previa.regras.length > 0 && (
        <>
          <div>
            <p className="font-medium text-amber-900 dark:text-amber-200">Forçar este horário vai quebrar estas regras:</p>
            <ul className="mt-1 list-disc space-y-0.5 pl-5 text-amber-900 dark:text-amber-100">
              {previa.regras.map((regra) => (
                <li key={regra}>{regra}</li>
              ))}
            </ul>
          </div>
          <label className="block">
            <span className={classeLabel}>Motivo (obrigatório)</span>
            <textarea className={classeInput} rows={2} maxLength={500} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          </label>
        </>
      )}

      {erro && (
        <p role="alert" className="text-red-700 dark:text-red-400">
          {erro}
        </p>
      )}

      <div className="flex flex-wrap justify-end gap-2">
        <button type="button" className={classeBotaoSecundario} onClick={aoDesistir}>
          Escolher outro horário
        </button>
        {previa && !previa.impedimento && previa.regras.length > 0 && (
          <button type="button" className={classeBotaoPrimario} disabled={!motivo.trim() || enviando} onClick={forcar}>
            {enviando ? "Forçando..." : "Forçar mesmo assim"}
          </button>
        )}
      </div>
    </div>
  );
}
