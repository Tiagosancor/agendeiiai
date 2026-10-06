import type { ReactNode } from "react";
import "./painel-operacionais.css";

/** Apresentação apenas: valores, filtros e ações pertencem aos módulos. */
export function CabecalhoOperacional({ titulo, descricao, children }: { titulo: string; descricao: string; children?: ReactNode }) {
  return <header className="painel-operacional-cabecalho">
    <div><h1>{titulo}</h1><p>{descricao}</p></div>
    {children && <div className="painel-operacional-acoes">{children}</div>}
  </header>;
}

export function IndicadorOperacional({ rotulo, valor, destaque = false, nomeAcessivel }: { rotulo: ReactNode; valor: ReactNode; destaque?: boolean; nomeAcessivel?: string }) {
  return <div className={`painel-operacional-indicador ${destaque ? "painel-operacional-indicador-destaque" : ""}`}>
    <p className="painel-operacional-indicador-rotulo">{rotulo}</p>
    <p className="painel-operacional-indicador-valor" aria-label={nomeAcessivel}>{valor}</p>
  </div>;
}
