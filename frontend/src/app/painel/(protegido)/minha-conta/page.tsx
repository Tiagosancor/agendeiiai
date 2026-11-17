"use client";

import { useState, type FormEvent } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { ErroApi, requisicaoAutenticacaoPainel } from "@/lib/api";
import { mensagemErroAcesso } from "@/lib/acesso";
import type { MinhaConta } from "@/lib/tipos";
import { CampoSenha } from "@/components/CampoSenha";
import { CabecalhoCadastro } from "@/components/painel/CadastrosVisuais";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel } from "@/components/estilos";

export default function PaginaMinhaConta() {
  const { minhaConta, atualizarMinhaConta } = useAutenticacao();
  const [erro, setErro] = useState("");
  return <div className="painel-cadastro space-y-5">
    <CabecalhoCadastro titulo="Minha conta" descricao="Gerencie seus dados de acesso e a recuperação da sua senha." />
    {minhaConta ? <FormulariosConta conta={minhaConta} /> : <div className={`${classeCartao} space-y-3 p-4`}>
      <p role="status">Carregando os dados da conta...</p>
      <button className={classeBotaoSecundario} onClick={() => atualizarMinhaConta().catch(() => setErro("Não foi possível carregar sua conta. Tente novamente."))}>Tentar novamente</button>
      {erro && <p role="alert">{erro}</p>}
    </div>}
  </div>;
}

function FormulariosConta({ conta }: { conta: MinhaConta }) {
  const { chamarApi, atualizarMinhaConta, entrar } = useAutenticacao();
  const [nomeUsuario, setNomeUsuario] = useState(conta.nomeUsuario ?? "");
  const [senhaUsuario, setSenhaUsuario] = useState("");
  const [email, setEmail] = useState(conta.email ?? "");
  const [senhaEmail, setSenhaEmail] = useState("");
  const [codigo, setCodigo] = useState("");
  const [pediuCodigo, setPediuCodigo] = useState(!!conta.emailPendenteMascarado);
  const [senhaAtual, setSenhaAtual] = useState("");
  const [novaSenha, setNovaSenha] = useState("");
  const [ocupado, setOcupado] = useState(false);
  const [erros, setErros] = useState<Record<string, unknown>>({});
  const [avisos, setAvisos] = useState<Record<string, string>>({});

  async function executar(secao: string, acao: () => Promise<void>, sucesso: string) {
    setOcupado(true); setErros({}); setAvisos({});
    try { await acao(); setAvisos({ [secao]: sucesso }); }
    catch (e) { setErros({ [secao]: e }); }
    finally { setOcupado(false); }
  }
  function resposta(secao: string) {
    return <>
      {erros[secao] != null && <p role="alert" className="text-sm text-red-600 dark:text-red-400">{mensagemErroAcesso(erros[secao])}</p>}
      {avisos[secao] && <p role="status" className="text-sm text-green-700 dark:text-green-400">{avisos[secao]}</p>}
    </>;
  }
  async function salvarUsuario(e: FormEvent) {
    e.preventDefault();
    await executar("usuario", async () => {
      await chamarApi("/painel/minha-conta/nome-usuario", { metodo: "PUT", corpo: { nomeUsuario: nomeUsuario.trim() || null, senhaAtual: senhaUsuario } });
      setSenhaUsuario(""); await atualizarMinhaConta();
    }, "Nome de usuário atualizado.");
  }
  async function pedirCodigo(e: FormEvent) {
    e.preventDefault();
    await executar("email", async () => {
      await chamarApi("/painel/minha-conta/email", { metodo: "POST", corpo: { email: email.trim(), senhaAtual: senhaEmail } });
      setPediuCodigo(true); setCodigo(""); await atualizarMinhaConta();
    }, "Solicitação recebida. Se for possível usar este e-mail, confira o código na sua caixa de entrada e no spam. O código vale 5 minutos.");
  }
  async function confirmar(e: FormEvent) {
    e.preventDefault();
    await executar("codigo", async () => {
      await chamarApi("/painel/minha-conta/email/confirmar", { metodo: "POST", corpo: { codigo } });
      setPediuCodigo(false); setCodigo(""); setSenhaEmail(""); await atualizarMinhaConta();
    }, "E-mail confirmado. Você já pode usá-lo para recuperar sua senha.");
  }
  async function trocarSenha(e: FormEvent) {
    e.preventDefault();
    await executar("senha", async () => {
      await chamarApi("/painel/minha-conta/senha", { metodo: "PUT", corpo: { senhaAtual, novaSenha } });
      const identificador = conta.nomeUsuario || conta.telefoneLogin || conta.email;
      try {
        if (!identificador) throw new Error("Sem identificador");
        await entrar(identificador, novaSenha);
      } catch {
        try { await requisicaoAutenticacaoPainel("logout"); } catch { /* As sessões anteriores já foram revogadas pela troca de senha. */ }
        // Recarregar descarta o token em memória e preserva o aviso, sem disputar o redirecionamento do layout.
        window.location.replace("/painel/login?senhaAlterada=1");
        return;
      }
      setSenhaAtual(""); setNovaSenha("");
    }, "Senha alterada. Sua sessão foi renovada com a nova senha.");
  }
  const erroUsuario = erros.usuario instanceof ErroApi ? erros.usuario : null;
  const sugestao = typeof erroUsuario?.corpo?.sugestao === "string" ? erroUsuario.corpo.sugestao : null;

  return <div className="space-y-5">
    <div className={`${classeCartao} p-4`}><p className="font-semibold">{conta.nome}</p><p className="text-sm">{conta.perfil}</p>{conta.telefoneLogin && <p className="mt-2 text-sm break-words">Telefone de login: {conta.telefoneLogin}</p>}</div>
    <section className={`${classeCartao} space-y-3 p-4`} aria-labelledby="conta-usuario">
      <h2 id="conta-usuario" className="font-semibold">Nome de usuário</h2>
      <form onSubmit={salvarUsuario} className="max-w-xl space-y-3">
        <label className={classeLabel} htmlFor="nome-usuario">Nome de usuário</label>
        <input id="nome-usuario" className={classeInput} value={nomeUsuario} onChange={(e) => setNomeUsuario(e.target.value)} autoComplete="username" autoCapitalize="none" spellCheck={false} aria-describedby="regra-usuario" aria-invalid={erroUsuario?.corpo?.campo === "nomeUsuario" || undefined} />
        <p id="regra-usuario" className="text-xs text-gray-500 dark:text-neutral-400">De 3 a 30 caracteres, com letra. Use letras sem acento, números, ponto, hífen ou sublinhado; sem sinais seguidos ou no final. Deixe em branco para remover se tiver outro acesso.</p>
        <CampoSenha rotulo="Senha atual para alterar usuário" required autoComplete="current-password" value={senhaUsuario} onChange={(e) => setSenhaUsuario(e.target.value)} className={classeInput} />
        {resposta("usuario")}
        {sugestao && <button type="button" className="text-sm underline" onClick={() => setNomeUsuario(sugestao)}>Usar {sugestao}</button>}
        <button disabled={ocupado} className={classeBotaoPrimario}>Salvar nome de usuário</button>
      </form>
    </section>
    <section className={`${classeCartao} space-y-3 p-4`} aria-labelledby="conta-email">
      <h2 id="conta-email" className="font-semibold">E-mail de recuperação</h2>
      <p className="text-sm break-words">{conta.email || "Nenhum e-mail cadastrado"} — {conta.emailVerificado ? "confirmado" : "não confirmado"}</p>
      <form onSubmit={pedirCodigo} className="max-w-xl space-y-3">
        <label htmlFor="email-conta" className={classeLabel}>E-mail</label>
        <input id="email-conta" required type="email" autoComplete="email" className={classeInput} value={email} onChange={(e) => { setEmail(e.target.value); }} />
        <CampoSenha rotulo="Senha atual para confirmar e-mail" required autoComplete="current-password" value={senhaEmail} onChange={(e) => setSenhaEmail(e.target.value)} className={classeInput} />
        {resposta("email")}
        <button disabled={ocupado} className={classeBotaoPrimario}>{pediuCodigo ? "Reenviar código" : "Solicitar código"}</button>
      </form>
      {pediuCodigo && <form onSubmit={confirmar} className="max-w-xl space-y-3">
        {conta.emailPendenteMascarado && <p className="text-sm">Solicitação pendente: {conta.emailPendenteMascarado}</p>}
        <label htmlFor="codigo-email" className={classeLabel}>Código de 6 dígitos</label>
        <input id="codigo-email" className={classeInput} required inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} value={codigo} onChange={(e) => setCodigo(e.target.value.replace(/\D/g, ""))} />
        <p className="text-xs">Cada código pode ser usado uma vez, com até 3 tentativas. Reenviar invalida o código anterior.</p>
        {resposta("codigo")}
        <button disabled={ocupado} className={classeBotaoPrimario}>Confirmar e-mail</button>
      </form>}
      {!pediuCodigo && resposta("codigo")}
    </section>
    <section className={`${classeCartao} space-y-3 p-4`} aria-labelledby="conta-senha">
      <h2 id="conta-senha" className="font-semibold">Trocar senha</h2>
      <form onSubmit={trocarSenha} className="max-w-xl space-y-3">
        <CampoSenha rotulo="Senha atual" required autoComplete="current-password" className={classeInput} value={senhaAtual} onChange={(e) => setSenhaAtual(e.target.value)} />
        <CampoSenha rotulo="Nova senha" required minLength={8} autoComplete="new-password" className={classeInput} value={novaSenha} onChange={(e) => setNovaSenha(e.target.value)} />
        <p className="text-xs">Pelo menos 8 caracteres, incluindo letra e número. Suas outras sessões serão encerradas.</p>
        {resposta("senha")}
        <button disabled={ocupado} className={classeBotaoPrimario}>Trocar senha</button>
      </form>
    </section>
  </div>;
}
