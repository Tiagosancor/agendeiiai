import { ErroApi } from "./api";

export interface IdentificadoresAcesso {
  email: string;
  nomeUsuario: string;
  telefoneLogin: string;
}

export function sugerirNomeUsuario(nome: string): string {
  const sugestao = nome.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase()
    .replace(/[^a-z0-9]+/g, ".").replace(/^\.+|\.+$/g, "").slice(0, 30).replace(/\.+$/g, "");
  return sugestao.length >= 3 && /[a-z]/.test(sugestao) ? sugestao : "";
}

export function validarIdentificadores(valor: IdentificadoresAcesso): string | null {
  return [valor.email, valor.nomeUsuario, valor.telefoneLogin].some((v) => v.trim())
    ? null : "Informe pelo menos um: e-mail, nome de usuário ou telefone de login.";
}

export function mensagemErroAcesso(erro: unknown): string {
  if (!(erro instanceof ErroApi)) return "Não foi possível salvar. Tente novamente.";
  if (erro.status === 429) return "Muitas tentativas. Aguarde alguns minutos e tente de novo.";
  if (erro.codigo === "senha_fraca") return "Use uma senha com pelo menos 8 caracteres, incluindo letra e número.";
  if (erro.codigo === "codigo_invalido") return "Código inválido ou vencido. Confira o código ou solicite outro.";
  return erro.message;
}

export function descreverAcesso(valor: { email: string | null; nomeUsuario: string | null; telefoneLogin: string | null }): string {
  return [valor.email, valor.nomeUsuario ? `@${valor.nomeUsuario}` : null, valor.telefoneLogin].filter(Boolean).join(" • ") || "Sem identificador de acesso";
}
