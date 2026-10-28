"use client";

import { useEffect, useRef, useState } from "react";
import { CabecalhoCadastro } from "@/components/painel/CadastrosVisuais";
import { classeBotaoPrimario } from "@/components/estilos";
import { useAutenticacao } from "@/lib/auth-context";
import { ErroApi } from "@/lib/api";
import type { ConexaoWhatsApp, StatusWhatsApp } from "@/lib/tipos";
import "@/components/painel/painel-negocio.css";

function conectado(status?: string) {
  return ["open", "conectada", "conectado"].includes(status?.toLowerCase() ?? "");
}

function aguardando(status?: string) {
  return ["aguardandoqrcode", "connecting", "conectando"].includes(status?.toLowerCase() ?? "");
}

function mensagemErro(erro: unknown) {
  if (erro instanceof ErroApi) {
    return typeof erro.corpo?.mensagem === "string" ? erro.corpo.mensagem : erro.message;
  }
  return "Não foi possível acessar o WhatsApp. Tente novamente.";
}

export default function PaginaWhatsApp() {
  const { temPermissao } = useAutenticacao();
  if (!temPermissao("GerenciarConfiguracoesDoNegocio")) {
    return <p>Sem permissão para gerenciar a conexão do WhatsApp.</p>;
  }
  return <ConexaoDoNegocio />;
}

function ConexaoDoNegocio() {
  const { chamarApi } = useAutenticacao();
  const [status, setStatus] = useState<StatusWhatsApp | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [ocupado, setOcupado] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [qrCode, setQrCode] = useState<string | null>(null);
  const [prazo, setPrazo] = useState<number | null>(null);
  const [expirou, setExpirou] = useState(false);
  const versao = useRef(0);
  const controle = useRef<AbortController | null>(null);
  const emOperacao = useRef(false);
  const montado = useRef(false);
  const chamarApiRef = useRef(chamarApi);

  useEffect(() => { chamarApiRef.current = chamarApi; }, [chamarApi]);

  useEffect(() => {
    montado.current = true;
    const atual = ++versao.current;
    const abortador = new AbortController();
    controle.current = abortador;
    chamarApiRef.current<StatusWhatsApp>("/painel/whatsapp/status", { sinal: abortador.signal }).then(dados => {
      if (atual !== versao.current) return;
      setStatus(dados);
    }).catch(e => {
      if (atual === versao.current && !(e instanceof ErroApi && e.status === 404)) setErro(mensagemErro(e));
    }).finally(() => {
      if (atual === versao.current) setCarregando(false);
    });
    return () => {
      montado.current = false;
      // Contador de requisições, não ref de DOM: invalida também operações iniciadas depois da montagem.
      // eslint-disable-next-line react-hooks/exhaustive-deps
      ++versao.current;
      abortador.abort(); controle.current?.abort();
    };
  }, []);

  useEffect(() => {
    if (prazo === null) return;
    const atual = versao.current;
    const abortador = new AbortController();
    let encerrado = false;
    let proxima: ReturnType<typeof setTimeout>;
    const expirar = setTimeout(() => {
      encerrado = true;
      abortador.abort();
      clearTimeout(proxima);
      if (atual !== versao.current) return;
      setPrazo(null); setQrCode(null); setExpirou(true);
    }, Math.max(0, prazo - Date.now()));
    async function consultar() {
      try {
        const dados = await chamarApiRef.current<StatusWhatsApp>("/painel/whatsapp/status", { sinal: abortador.signal });
        if (encerrado || atual !== versao.current) return;
        setStatus(dados); setErro(null);
        if (!aguardando(dados.status)) {
          encerrado = true;
          clearTimeout(expirar);
          setPrazo(null); setQrCode(null);
          return;
        }
      } catch (e) {
        if (encerrado || atual !== versao.current) return;
        if (!(e instanceof ErroApi && e.status === 404)) setErro(mensagemErro(e));
      }
      if (!encerrado && atual === versao.current) proxima = setTimeout(consultar, 3000);
    }
    proxima = setTimeout(consultar, 3000);
    return () => { encerrado = true; abortador.abort(); clearTimeout(proxima); clearTimeout(expirar); };
  }, [prazo]);

  async function conectar() {
    if (emOperacao.current) return;
    emOperacao.current = true;
    const atual = ++versao.current;
    controle.current?.abort();
    const abortador = new AbortController();
    controle.current = abortador;
    setOcupado(true); setErro(null); setPrazo(null); setQrCode(null); setExpirou(false);
    try {
      const dados = await chamarApiRef.current<ConexaoWhatsApp>("/painel/whatsapp/conectar", { metodo: "POST", sinal: abortador.signal });
      if (atual !== versao.current) return;
      // O POST não traz telefone nem agenteAtivo: consulte o estado completo.
      if (conectado(dados.status)) {
        const completo = await chamarApiRef.current<StatusWhatsApp>("/painel/whatsapp/status", { sinal: abortador.signal });
        if (atual === versao.current) setStatus(completo);
      } else {
        setStatus(anterior => ({ status: dados.status, agenteAtivo: anterior?.agenteAtivo ?? false, telefone: null }));
        setQrCode(dados.qrCode);
        if (aguardando(dados.status) && dados.qrCode) setPrazo(anterior => anterior ?? Date.now() + 120_000);
      }
    } catch (e) {
      if (atual === versao.current) setErro(mensagemErro(e));
    } finally {
      emOperacao.current = false;
      if (montado.current) setOcupado(false);
    }
  }

  async function alternarAgente() {
    if (!status || emOperacao.current) return;
    emOperacao.current = true;
    const atual = versao.current;
    const ativo = !status.agenteAtivo;
    setOcupado(true); setErro(null);
    try {
      await chamarApiRef.current<void>("/painel/whatsapp/agente", { metodo: "PUT", corpo: { ativo } });
      if (atual === versao.current) setStatus(anterior => anterior ? { ...anterior, agenteAtivo: ativo } : anterior);
    } catch (e) {
      if (atual === versao.current) setErro(mensagemErro(e));
    } finally {
      emOperacao.current = false;
      if (montado.current) setOcupado(false);
    }
  }

  const estaConectado = conectado(status?.status);
  return <div className="painel-cadastro painel-negocio">
    <CabecalhoCadastro titulo="WhatsApp" descricao="Conecte o WhatsApp do seu negócio e controle a ativação do agente." />
    <section className="painel-negocio-secao space-y-4" aria-label="Conexão do WhatsApp">
      <p role="status" className="font-semibold">{carregando ? "Carregando…" : estaConectado ? "Conectado" : expirou ? "O código expirou" : aguardando(status?.status) ? qrCode ? "Aguardando leitura do QR Code" : "Gere um novo QR para continuar" : status ? "Desconectado/indisponível" : erro ? "Situação desconhecida" : "Ainda não conectado"}{ocupado && estaConectado ? " — Salvando…" : ""}</p>
      {!carregando && <>
        {estaConectado ? <>
          {status?.telefone && <p className="break-all">Número: {status.telefone}</p>}
          <button type="button" role="switch" aria-label="Agente ativo" aria-checked={status?.agenteAtivo ?? false} disabled={ocupado} onClick={alternarAgente} className={`${classeBotaoPrimario} min-h-11`}>
            <span aria-hidden="true">Agente ativo: {status?.agenteAtivo ? "sim" : "não"}{ocupado ? " — Salvando…" : ""}</span>
          </button>
        </> : <>
          {qrCode && <div className="space-y-3">
            {/* Data URI temporária do provedor; não passa pela otimização de imagens. */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={qrCode} alt="QR Code para conectar o WhatsApp do negócio" width={280} height={280} className="h-auto max-w-full rounded-lg bg-white p-3" />
            <p>No celular, abra WhatsApp &gt; Aparelhos conectados &gt; Conectar um aparelho e leia o código.</p>
          </div>}
          <button type="button" disabled={ocupado} onClick={conectar} className={`${classeBotaoPrimario} min-h-11`}>
            {ocupado ? "Conectando…" : qrCode || expirou || aguardando(status?.status) ? "Gerar novo QR" : "Conectar WhatsApp"}
          </button>
        </>}
      </>}
      {erro && <p role="alert" className="text-sm text-red-600 dark:text-red-400">{erro}</p>}
    </section>
  </div>;
}
