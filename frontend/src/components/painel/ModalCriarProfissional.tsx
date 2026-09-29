"use client";

import { useEffect, useState, type FormEvent } from "react";
import Link from "next/link";
import { useAutenticacao } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { ACESSO_INICIAL, CamposAcesso, acessoParaApi, type EstadoAcesso } from "@/components/painel/CamposAcesso";
import { classeBotaoPrimario, classeBotaoSecundario, classeInput, classeLabel } from "@/components/estilos";
import { ErroApi } from "@/lib/api";

/** Dados que já vêm de "Novo usuário, perfil Profissional" (seção 7): o profissional nasce vinculado a esse usuário. */
export interface ProfissionalPreenchido {
  usuarioId: string;
  nome: string;
  telefone: string;
  email: string;
}

/**
 * Novo profissional (seção 7). Com a permissão de gerenciar usuários, pergunta se ele vai ter acesso ao
 * sistema; vindo de "Usuários", já nasce vinculado ao usuário recém-criado (sem a pergunta).
 */
export function ModalCriarProfissional({
  aberto,
  aoFechar,
  aoCriar,
  preenchido = null,
}: {
  aberto: boolean;
  aoFechar: () => void;
  aoCriar: (profissionalId: string) => Promise<void>;
  preenchido?: ProfissionalPreenchido | null;
}) {
  const { chamarApi, temPermissao } = useAutenticacao();
  const podeDarAcesso = temPermissao("GerenciarUsuarios");
  const [nome, setNome] = useState("");
  const [funcao, setFuncao] = useState("");
  const [telefone, setTelefone] = useState("");
  const [email, setEmail] = useState("");
  const [cpf, setCpf] = useState("");
  const [acesso, setAcesso] = useState<EstadoAcesso>(ACESSO_INICIAL);
  const [erro, setErro] = useState<string | null>(null);
  const [limiteDoPlano, setLimiteDoPlano] = useState(false);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    if (!aberto) return;
    // Recomeça a cada abertura, com o que veio do cadastro de usuário (se veio).
    /* eslint-disable react-hooks/set-state-in-effect */
    setNome(preenchido?.nome ?? "");
    setFuncao("");
    setTelefone(preenchido?.telefone ?? "");
    setEmail(preenchido?.email ?? "");
    setCpf("");
    setAcesso(ACESSO_INICIAL);
    setErro(null);
    setLimiteDoPlano(false);
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [aberto, preenchido]);

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      const id = await chamarApi<string>("/painel/profissionais", {
        metodo: "POST",
        corpo: {
          nome,
          telefone: telefone || null,
          email: email || null,
          cpf: cpf || null,
          funcao: funcao || null,
          acesso: preenchido ? null : acessoParaApi(acesso),
          usuarioId: preenchido?.usuarioId ?? null,
        },
      });
      await aoCriar(id);
    } catch (excecao) {
      const limite = excecao instanceof ErroApi && excecao.codigo === "limite_profissionais";
      setLimiteDoPlano(limite);
      setErro(
        limite || (excecao instanceof ErroApi && (excecao.status === 400 || excecao.status === 409))
          ? (excecao as ErroApi).message
          : "Não foi possível criar o profissional.",
      );
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo="Novo profissional" aberto={aberto} aoFechar={aoFechar}>
      <form onSubmit={aoEnviar} className="space-y-3">
        {preenchido && (
          <p className="rounded-lg bg-gray-50 px-3 py-2 text-sm text-gray-700 dark:bg-neutral-800 dark:text-neutral-300">
            Complete o cadastro de profissional de <strong>{preenchido.nome}</strong>. Ele vai entrar no sistema com {preenchido.email}.
          </p>
        )}
        <label>
          <span className={classeLabel}>Nome</span>
          <input required className={classeInput} value={nome} onChange={(e) => setNome(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Função (opcional, aparece na página pública)</span>
          <input className={classeInput} value={funcao} onChange={(e) => setFuncao(e.target.value)} placeholder="Barbeiro" />
        </label>
        <label>
          <span className={classeLabel}>Telefone (opcional)</span>
          <input className={classeInput} value={telefone} onChange={(e) => setTelefone(e.target.value)} type="tel" placeholder="(71) 98888-7777" />
        </label>
        <label>
          <span className={classeLabel}>E-mail (opcional)</span>
          <input
            type="email"
            className={classeInput}
            value={email}
            onChange={(e) => {
              setEmail(e.target.value);
              // O e-mail de acesso acompanha o do cadastro enquanto ninguém o mudou à parte.
              if (acesso.email === email) setAcesso({ ...acesso, email: e.target.value });
            }}
          />
        </label>
        <label>
          <span className={classeLabel}>CPF (opcional)</span>
          <input className={classeInput} value={cpf} onChange={(e) => setCpf(e.target.value)} placeholder="000.000.000-00" />
        </label>

        {podeDarAcesso && !preenchido && <CamposAcesso valor={acesso} aoMudar={setAcesso} />}

        {erro && (limiteDoPlano ? <AvisoLimitePlano mensagem={erro} /> : <p className="text-sm text-red-600">{erro}</p>)}

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

/** Limite de profissionais do plano (seção 7): mensagem clara e o caminho para mudar de plano. */
export function AvisoLimitePlano({ mensagem }: { mensagem: string }) {
  return (
    <div role="alert" className="mb-4 rounded-lg border border-marca-acento/40 bg-marca-acento/10 px-3 py-2 text-sm text-gray-800 dark:text-neutral-200">
      {mensagem}{" "}
      <Link href="/painel/assinatura" className="font-semibold underline">
        Mudar de plano
      </Link>
    </div>
  );
}
