import type { ReactNode } from "react";
import "./painel-cadastros.css";

/** Apresentação compartilhada; dados, permissões e ações permanecem nas páginas. */
export function CabecalhoCadastro({ titulo, descricao, children }: { titulo: string; descricao: string; children?: ReactNode }) {
  return <header className="painel-cadastro-cabecalho">
    <div><h1>{titulo}</h1><p>{descricao}</p></div>
    {children && <div className="painel-cadastro-acoes-principais">{children}</div>}
  </header>;
}

export function StatusCadastro({ ativo }: { ativo: boolean }) {
  return <span className={`painel-cadastro-status ${ativo ? "painel-cadastro-status-ativo" : ""}`}>
    <span aria-hidden="true" />{ativo ? "Ativo" : "Inativo"}
  </span>;
}

export function CarregandoCadastro() {
  return <p role="status" className="painel-cadastro-carregando">Carregando...</p>;
}
