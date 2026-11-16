"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { CampoSenha } from "@/components/CampoSenha";
import { classeBotaoPrimario, classeBotaoSecundario, classeInput } from "@/components/estilos";
import { CamposIdentificadores } from "./CamposIdentificadores";
import { useAutenticacao } from "@/lib/auth-context";
import { descreverAcesso, mensagemErroAcesso, validarIdentificadores } from "@/lib/acesso";
import type { AcessoVinculado } from "@/lib/tipos";

/** Independente do formulário de dados pessoais: cada ação tem sua própria gravação. */
export function GerenciarAcessoUsuario({ acesso, telefoneContato, aoSalvar }: {
  acesso: AcessoVinculado;
  telefoneContato?: string;
  aoSalvar: () => Promise<void>;
}) {
  const { chamarApi, temPermissao, usuarioId } = useAutenticacao();
  const [editando, setEditando] = useState(false);
  const [definindoSenha, setDefinindoSenha] = useState(false);
  const [campos, setCampos] = useState({ email: acesso.email ?? "", nomeUsuario: acesso.nomeUsuario ?? "", telefoneLogin: acesso.telefoneLogin ?? "" });
  const [senha, setSenha] = useState("");
  const [erro, setErro] = useState<unknown>(null);
  const [aviso, setAviso] = useState("");
  const [ocupado, setOcupado] = useState(false);
  const gerenciar = temPermissao("GerenciarUsuarios");

  async function executar(acao: () => Promise<void>, sucesso: string) {
    setErro(null); setAviso(""); setOcupado(true);
    try { await acao(); setAviso(sucesso); } catch (e) { setErro(e); } finally { setOcupado(false); }
  }
  async function salvar(e: FormEvent) {
    e.preventDefault();
    const invalido = validarIdentificadores(campos);
    if (invalido) { setErro(new Error(invalido)); return; }
    await executar(async () => {
      await chamarApi(`/painel/usuarios/${acesso.usuarioId}/acesso`, { metodo: "PUT", corpo: {
        email: campos.email.trim() || null, nomeUsuario: campos.nomeUsuario.trim() || null, telefoneLogin: campos.telefoneLogin.trim() || null,
      } });
      setEditando(false); await aoSalvar();
    }, "Acesso atualizado. Um e-mail alterado precisa ser confirmado.");
  }
  async function definirSenha(e: FormEvent) {
    e.preventDefault();
    if (!confirm("Definir uma nova senha e encerrar todas as sessões deste usuário?")) return;
    await executar(async () => {
      await chamarApi(`/painel/usuarios/${acesso.usuarioId}/senha`, { metodo: "PUT", corpo: { novaSenha: senha } });
      setSenha(""); setDefinindoSenha(false);
    }, "Nova senha definida. As sessões anteriores foram encerradas.");
  }
  return <section className="space-y-3 rounded-lg border border-gray-200 p-3 dark:border-neutral-800">
    <h3 className="font-semibold">Acesso</h3>
    <p className="break-words">Entra com {descreverAcesso(acesso)}{!acesso.ativo && " (acesso desativado)"}.</p>
    {acesso.email && <p className="text-sm">E-mail: {acesso.emailVerificado ? "confirmado" : "não confirmado"}</p>}
    {aviso && <p role="status" className="text-sm text-green-700 dark:text-green-400">{aviso}</p>}
    {erro != null && <p role="alert" className="text-sm text-red-600 dark:text-red-400">{erro instanceof Error && !("status" in erro) ? erro.message : mensagemErroAcesso(erro)}</p>}
    <div className="flex flex-wrap gap-2">
      {gerenciar && temPermissao("EditarCadastros") && !editando && <button disabled={ocupado} className={classeBotaoSecundario} type="button" onClick={() => {
        setCampos({ email: acesso.email ?? "", nomeUsuario: acesso.nomeUsuario ?? "", telefoneLogin: acesso.telefoneLogin ?? "" }); setErro(null); setEditando(true); setDefinindoSenha(false);
      }}>Editar acesso</button>}
      {gerenciar && acesso.usuarioId !== usuarioId && !definindoSenha && <button disabled={ocupado} type="button" className={classeBotaoSecundario} onClick={() => { setDefinindoSenha(true); setEditando(false); setErro(null); }}>Definir nova senha</button>}
      {gerenciar && <button disabled={ocupado || !acesso.email} className={classeBotaoSecundario} type="button" onClick={() => executar(async () => {
        await chamarApi(`/painel/usuarios/${acesso.usuarioId}/convite`, { metodo: "POST" });
      }, "Convite solicitado. Oriente o usuário a conferir o e-mail.")}>Enviar convite</button>}
      {acesso.usuarioId === usuarioId && <Link href="/painel/minha-conta" className="text-sm underline">Alterar minha senha em Minha conta</Link>}
    </div>
    {!acesso.email && gerenciar && <p className="text-xs text-gray-500 dark:text-neutral-400">Adicione um e-mail para enviar convite.</p>}
    {editando && <form onSubmit={salvar} className="space-y-3">
      <CamposIdentificadores valor={campos} aoMudar={setCampos} telefoneContato={telefoneContato} erro={erro} />
      <div className="flex flex-wrap gap-2"><button disabled={ocupado} className={classeBotaoPrimario}>Salvar acesso</button><button disabled={ocupado} type="button" className={classeBotaoSecundario} onClick={() => setEditando(false)}>Cancelar</button></div>
    </form>}
    {definindoSenha && <form onSubmit={definirSenha} className="space-y-3">
      <CampoSenha rotulo="Nova senha do usuário" required minLength={8} autoComplete="new-password" value={senha} onChange={(e) => setSenha(e.target.value)} className={classeInput} />
      <p className="text-xs">Pelo menos 8 caracteres, incluindo letra e número.</p>
      <div className="flex flex-wrap gap-2"><button disabled={ocupado} className={classeBotaoPrimario}>Salvar nova senha</button><button disabled={ocupado} type="button" className={classeBotaoSecundario} onClick={() => { setDefinindoSenha(false); setSenha(""); }}>Cancelar</button></div>
    </form>}
  </section>;
}
