"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { classeBotaoPrimario, classeInput, classeLabel } from "@/components/estilos";
import type { ProgramaFidelidadeResumo } from "@/lib/tipos";

export default function PaginaFidelidade() {
  const { chamarApi } = useAutenticacao();
  const [selosNecessarios, setSelosNecessarios] = useState("10");
  const [descricaoRecompensa, setDescricaoRecompensa] = useState("");
  const [ativo, setAtivo] = useState<boolean | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [mensagemSucesso, setMensagemSucesso] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  const carregar = useCallback(async () => {
    try {
      const programa = await chamarApi<ProgramaFidelidadeResumo>("/painel/fidelidade");
      setSelosNecessarios(String(programa.selosNecessarios));
      setDescricaoRecompensa(programa.descricaoRecompensa);
      setAtivo(programa.ativo);
    } catch (excecao) {
      // 404 = nenhum programa configurado ainda, não é erro de verdade.
      if (excecao instanceof ErroApi && excecao.status !== 404) {
        setErro("Não foi possível carregar o programa de fidelidade.");
      }
      setAtivo(false);
    }
  }, [chamarApi]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  async function salvar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setMensagemSucesso(null);
    setSalvando(true);
    try {
      await chamarApi("/painel/fidelidade", {
        metodo: "PUT",
        corpo: { selosNecessarios: Number(selosNecessarios), descricaoRecompensa },
      });
      setMensagemSucesso("Programa de fidelidade salvo.");
      await carregar();
    } catch {
      setErro("Não foi possível salvar o programa de fidelidade.");
    } finally {
      setSalvando(false);
    }
  }

  return (
    <div className="max-w-lg space-y-4">
      <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Fidelidade</h1>
      <p className="text-sm text-gray-500 dark:text-neutral-400">
        Cartão de selos: a cada atendimento concluído, o cliente ganha um selo. Ao atingir o número
        configurado abaixo, a recepção pode resgatar a recompensa na ficha do cliente.
      </p>

      <form onSubmit={salvar} className="space-y-3">
        <label>
          <span className={classeLabel}>Selos necessários para resgatar</span>
          <input
            required
            type="number"
            min={1}
            className={classeInput}
            value={selosNecessarios}
            onChange={(e) => setSelosNecessarios(e.target.value)}
          />
        </label>
        <label>
          <span className={classeLabel}>Recompensa</span>
          <input
            required
            className={classeInput}
            value={descricaoRecompensa}
            onChange={(e) => setDescricaoRecompensa(e.target.value)}
            placeholder="Ex.: Um corte grátis"
          />
        </label>

        {ativo === false && descricaoRecompensa === "" && (
          <p className="text-xs text-gray-500 dark:text-neutral-400">Nenhum programa configurado ainda.</p>
        )}
        {erro && <p className="text-sm text-red-600">{erro}</p>}
        {mensagemSucesso && <p className="text-sm text-green-600">{mensagemSucesso}</p>}

        <button type="submit" disabled={salvando} className={classeBotaoPrimario}>
          {salvando ? "Salvando..." : "Salvar"}
        </button>
      </form>
    </div>
  );
}
