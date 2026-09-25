"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import type { CupomResumo } from "@/lib/tipos";
import { formatarReais } from "@/lib/formatacao";

export default function PaginaCupons() {
  const { chamarApi } = useAutenticacao();
  const [cupons, setCupons] = useState<CupomResumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [modalAberto, setModalAberto] = useState(false);

  const carregar = useCallback(async () => {
    try {
      setCupons(await chamarApi<CupomResumo[]>("/painel/cupons"));
    } catch {
      setErro("Não foi possível carregar os cupons.");
    }
  }, [chamarApi]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- busca disparada pela montagem.
    carregar();
  }, [carregar]);

  async function alternarAtivo(cupom: CupomResumo) {
    const acao = cupom.ativo ? "desativar" : "ativar";
    await chamarApi(`/painel/cupons/${cupom.id}/${acao}`, { metodo: "POST" });
    await carregar();
  }

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Cupons</h1>
        <button className={classeBotaoPrimario} onClick={() => setModalAberto(true)}>
          Novo cupom
        </button>
      </div>

      {erro && <p className="mb-4 text-sm text-red-600">{erro}</p>}

      <div className={classeCartao}>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[650px]">
            <thead className="border-b border-gray-200 dark:border-neutral-800">
              <tr>
                <th className={classeTh}>Código</th>
                <th className={classeTh}>Desconto</th>
                <th className={classeTh}>Validade</th>
                <th className={classeTh}>Usos</th>
                <th className={classeTh}>Status</th>
                <th className={classeTh}>Ações</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 dark:divide-neutral-800">
              {cupons?.map((cupom) => (
                <tr key={cupom.id}>
                  <td className={classeTd}>{cupom.codigo}</td>
                  <td className={classeTd}>{cupom.tipo === "Percentual" ? `${cupom.valor}%` : formatarReais(cupom.valor)}</td>
                  <td className={classeTd}>{cupom.validoAte ? new Date(cupom.validoAte).toLocaleDateString("pt-BR") : "Sem validade"}</td>
                  <td className={classeTd}>
                    {cupom.usosAtuais}
                    {cupom.limiteUsos ? ` / ${cupom.limiteUsos}` : ""}
                  </td>
                  <td className={classeTd}>{cupom.ativo ? "Ativo" : "Inativo"}</td>
                  <td className={classeTd}>
                    <button className="text-gray-600 hover:underline dark:text-neutral-300" onClick={() => alternarAtivo(cupom)}>
                      {cupom.ativo ? "Desativar" : "Ativar"}
                    </button>
                  </td>
                </tr>
              ))}
              {cupons?.length === 0 && (
                <tr>
                  <td className={classeTd} colSpan={6}>
                    Nenhum cupom cadastrado ainda.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      <ModalNovoCupom
        aberto={modalAberto}
        aoFechar={() => setModalAberto(false)}
        aoCriar={async () => {
          setModalAberto(false);
          await carregar();
        }}
      />
    </div>
  );
}

function ModalNovoCupom({ aberto, aoFechar, aoCriar }: { aberto: boolean; aoFechar: () => void; aoCriar: () => Promise<void> }) {
  const { chamarApi } = useAutenticacao();
  const [codigo, setCodigo] = useState("");
  const [tipo, setTipo] = useState<"Percentual" | "ValorFixo">("Percentual");
  const [valor, setValor] = useState("");
  const [validoAte, setValidoAte] = useState("");
  const [limiteUsos, setLimiteUsos] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await chamarApi("/painel/cupons", {
        metodo: "POST",
        corpo: {
          codigo,
          tipo,
          valor: Number(valor),
          validoAte: validoAte ? new Date(validoAte).toISOString() : null,
          limiteUsos: limiteUsos ? Number(limiteUsos) : null,
        },
      });
      setCodigo("");
      setValor("");
      setValidoAte("");
      setLimiteUsos("");
      await aoCriar();
    } catch {
      setErro("Não foi possível criar o cupom (confira se o código já existe).");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo="Novo cupom" aberto={aberto} aoFechar={aoFechar}>
      <form onSubmit={aoEnviar} className="space-y-3">
        <label>
          <span className={classeLabel}>Código</span>
          <input required className={classeInput} value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="BEMVINDO10" />
        </label>
        <div className="flex gap-3">
          <label className="flex-1">
            <span className={classeLabel}>Tipo</span>
            <select className={classeInput} value={tipo} onChange={(e) => setTipo(e.target.value as "Percentual" | "ValorFixo")}>
              <option value="Percentual">Percentual</option>
              <option value="ValorFixo">Valor fixo (R$)</option>
            </select>
          </label>
          <label className="flex-1">
            <span className={classeLabel}>Valor</span>
            <input required type="number" min={0} step="0.01" className={classeInput} value={valor} onChange={(e) => setValor(e.target.value)} />
          </label>
        </div>
        <div className="flex gap-3">
          <label className="flex-1">
            <span className={classeLabel}>Válido até (opcional)</span>
            <input type="date" className={classeInput} value={validoAte} onChange={(e) => setValidoAte(e.target.value)} />
          </label>
          <label className="flex-1">
            <span className={classeLabel}>Limite de usos (opcional)</span>
            <input type="number" min={1} className={classeInput} value={limiteUsos} onChange={(e) => setLimiteUsos(e.target.value)} />
          </label>
        </div>

        {erro && <p className="text-sm text-red-600">{erro}</p>}

        <div className="flex justify-end gap-2 pt-2">
          <button type="button" className={classeBotaoSecundario} onClick={aoFechar}>
            Cancelar
          </button>
          <button type="submit" disabled={enviando} className={classeBotaoPrimario}>
            {enviando ? "Criando..." : "Criar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
