"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { classeBotaoPrimario, classeBotaoSecundario, classeInput, classeLabel } from "@/components/estilos";
import { ErroApi, useAutenticacao } from "@/lib/auth-context";
import type { ConfiguracaoLembreteRetorno, RelatorioLembreteRetorno } from "@/lib/tipos";
import { criarLembreteRetornoService } from "@/services/lembrete-retorno-service";

const VARIAVEIS = ["{nome_cliente}", "{nome_empresa}", "{dias}", "{link_agendamento}"] as const;
type CampoConfiguracao = keyof ConfiguracaoLembreteRetorno;
type ErrosCampos = Partial<Record<CampoConfiguracao, string>>;

function extrairErrosBackend(erro: unknown): ErrosCampos {
  if (!(erro instanceof ErroApi) || erro.status !== 400) return {};
  const erros = erro.corpo?.errors;
  if (!erros || typeof erros !== "object") return {};
  return Object.fromEntries(Object.entries(erros).flatMap(([campo, mensagens]) => {
    const primeira = Array.isArray(mensagens) ? mensagens.find((item): item is string => typeof item === "string") : null;
    return primeira ? [[campo.toLowerCase(), primeira]] : [];
  })) as ErrosCampos;
}

function validar(configuracao: ConfiguracaoLembreteRetorno): ErrosCampos {
  const erros: ErrosCampos = {};
  if (!Number.isInteger(configuracao.dias) || configuracao.dias < 1 || configuracao.dias > 365) erros.dias = "Informe um período entre 1 e 365 dias.";
  if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(configuracao.horario.slice(0, 5))) erros.horario = "Informe um horário válido no formato HH:mm.";
  if (!configuracao.mensagem.trim()) erros.mensagem = "Informe a mensagem do lembrete.";
  else if (configuracao.mensagem.length > 1000) erros.mensagem = "A mensagem deve ter no máximo 1000 caracteres.";
  else {
    const desconhecidas = [...configuracao.mensagem.matchAll(/\{[^{}]+\}/g)]
      .map(([variavel]) => variavel)
      .filter((variavel) => !VARIAVEIS.includes(variavel as typeof VARIAVEIS[number]));
    if (desconhecidas.length > 0) erros.mensagem = `Variável não reconhecida: ${[...new Set(desconhecidas)].join(", ")}.`;
  }
  return erros;
}

function criarPrevia(configuracao: ConfiguracaoLembreteRetorno) {
  return configuracao.mensagem
    .replaceAll("{nome_cliente}", "Ana")
    .replaceAll("{nome_empresa}", "Studio Exemplo")
    .replaceAll("{dias}", String(configuracao.dias))
    .replaceAll("{link_agendamento}", "studio.exemplo.com.br");
}

export function LembreteRetorno() {
  const { chamarApi } = useAutenticacao();
  const service = useMemo(() => criarLembreteRetornoService(chamarApi), [chamarApi]);
  const mensagemRef = useRef<HTMLTextAreaElement>(null);
  const [configuracao, setConfiguracao] = useState<ConfiguracaoLembreteRetorno | null>(null);
  const [original, setOriginal] = useState<ConfiguracaoLembreteRetorno | null>(null);
  const [relatorio, setRelatorio] = useState<RelatorioLembreteRetorno | null>(null);
  const [errosCampos, setErrosCampos] = useState<ErrosCampos>({});
  const [erro, setErro] = useState<string | null>(null);
  const [erroRelatorio, setErroRelatorio] = useState<string | null>(null);
  const [sucesso, setSucesso] = useState<string | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [carregandoRelatorio, setCarregandoRelatorio] = useState(true);
  const [salvando, setSalvando] = useState(false);

  const carregarConfiguracao = useCallback(async () => {
    setCarregando(true);
    try {
      setErro(null);
      const atual = await service.obterConfiguracao();
      setConfiguracao(atual);
      setOriginal(atual);
    } catch {
      setErro("Não foi possível carregar o lembrete de retorno.");
    } finally {
      setCarregando(false);
    }
  }, [service]);

  const carregarRelatorio = useCallback(async () => {
    setCarregandoRelatorio(true);
    try {
      setErroRelatorio(null);
      setRelatorio(await service.obterRelatorio(30));
    } catch {
      setErroRelatorio("Não foi possível carregar os resultados do lembrete.");
    } finally {
      setCarregandoRelatorio(false);
    }
  }, [service]);

  useEffect(() => {
    // Buscas disparadas pela montagem, não estado derivado de props.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregarConfiguracao();
    carregarRelatorio();
  }, [carregarConfiguracao, carregarRelatorio]);

  function atualizar<K extends CampoConfiguracao>(campo: K, valor: ConfiguracaoLembreteRetorno[K]) {
    setConfiguracao((atual) => atual ? { ...atual, [campo]: valor } : atual);
    setErrosCampos((atuais) => ({ ...atuais, [campo]: undefined }));
    setSucesso(null);
  }

  function inserirVariavel(variavel: typeof VARIAVEIS[number]) {
    if (!configuracao || !configuracao.ativo) return;
    const campo = mensagemRef.current;
    const inicio = campo?.selectionStart ?? configuracao.mensagem.length;
    const fim = campo?.selectionEnd ?? inicio;
    atualizar("mensagem", configuracao.mensagem.slice(0, inicio) + variavel + configuracao.mensagem.slice(fim));
    requestAnimationFrame(() => {
      campo?.focus();
      campo?.setSelectionRange(inicio + variavel.length, inicio + variavel.length);
    });
  }

  async function salvar(evento: FormEvent) {
    evento.preventDefault();
    if (!configuracao) return;
    const errosLocais = validar(configuracao);
    setErrosCampos(errosLocais);
    setErro(null);
    setSucesso(null);
    if (Object.keys(errosLocais).length > 0) return;
    setSalvando(true);
    try {
      await service.salvarConfiguracao(configuracao);
      setOriginal(configuracao);
      setSucesso("Configuração do lembrete atualizada.");
    } catch (falha) {
      const campos = extrairErrosBackend(falha);
      setErrosCampos(campos);
      if (Object.keys(campos).length === 0) setErro("Não foi possível salvar o lembrete de retorno.");
    } finally {
      setSalvando(false);
    }
  }

  const alterada = configuracao !== null && original !== null && JSON.stringify(configuracao) !== JSON.stringify(original);
  const reativando = original?.ativo === false && configuracao?.ativo === true;

  return (
    <section className="painel-negocio-secao painel-lembrete-retorno" aria-labelledby="titulo-lembrete-retorno">
      <div className="painel-lembrete-cabecalho">
        <div>
          <p className="painel-lembrete-trilha">Configurações <span aria-hidden="true">/</span> Notificações</p>
          <h2 id="titulo-lembrete-retorno">Lembrete de retorno</h2>
          <p>Convide o cliente a voltar pelo WhatsApp depois do último atendimento concluído.</p>
        </div>
        {configuracao && <span className={`painel-lembrete-estado ${configuracao.ativo ? "painel-lembrete-estado-ativo" : ""}`}><span aria-hidden="true" />{configuracao.ativo ? "Ativo" : "Desativado"}</span>}
      </div>

      {carregando && <p className="painel-lembrete-carregando">Carregando configuração...</p>}
      {!carregando && !configuracao && erro && <p role="alert" className="painel-lembrete-erro">{erro}</p>}

      {configuracao && <div className="painel-lembrete-grade">
        <form onSubmit={salvar} noValidate className="painel-lembrete-formulario">
          <label className="painel-lembrete-ativacao">
            <input type="checkbox" role="switch" checked={configuracao.ativo} onChange={(e) => atualizar("ativo", e.target.checked)} />
            <span><strong>Ativar lembrete de retorno</strong><small>O envio acontece automaticamente no horário configurado.</small></span>
          </label>

          {reativando && <p className="painel-lembrete-aviso">Ao reativar, não serão enviados lembretes de datas passadas.</p>}

          <div className="painel-lembrete-campos">
            <label>
              <span className={classeLabel}>Dias após o atendimento</span>
              <input className={classeInput} type="number" min={1} max={365} required disabled={!configuracao.ativo} value={configuracao.dias} aria-invalid={!!errosCampos.dias} aria-describedby={errosCampos.dias ? "erro-lembrete-dias" : undefined} onChange={(e) => atualizar("dias", Number(e.target.value))} />
              {errosCampos.dias && <small id="erro-lembrete-dias" className="painel-lembrete-erro">{errosCampos.dias}</small>}
            </label>
            <label>
              <span className={classeLabel}>Horário de envio</span>
              <input className={classeInput} type="time" required disabled={!configuracao.ativo} value={configuracao.horario.slice(0, 5)} aria-invalid={!!errosCampos.horario} aria-describedby={errosCampos.horario ? "erro-lembrete-horario" : "ajuda-lembrete-horario"} onChange={(e) => atualizar("horario", e.target.value)} />
              <small id="ajuda-lembrete-horario">Usa o fuso horário do negócio.</small>
              {errosCampos.horario && <small id="erro-lembrete-horario" className="painel-lembrete-erro">{errosCampos.horario}</small>}
            </label>
          </div>

          <label>
            <span className={classeLabel}>Mensagem</span>
            <textarea ref={mensagemRef} className={classeInput} rows={5} required disabled={!configuracao.ativo} maxLength={1000} value={configuracao.mensagem} aria-invalid={!!errosCampos.mensagem} aria-describedby={errosCampos.mensagem ? "erro-lembrete-mensagem" : "ajuda-lembrete-mensagem"} onChange={(e) => atualizar("mensagem", e.target.value)} />
            <span id="ajuda-lembrete-mensagem" className="painel-lembrete-variaveis">Inserir variável:{VARIAVEIS.map((variavel) => <button key={variavel} type="button" disabled={!configuracao.ativo} onClick={() => inserirVariavel(variavel)}>{variavel}</button>)}</span>
            <small className="painel-lembrete-contagem">{configuracao.mensagem.length}/1000</small>
            {errosCampos.mensagem && <small id="erro-lembrete-mensagem" className="painel-lembrete-erro">{errosCampos.mensagem}</small>}
          </label>

          <div className="painel-lembrete-previa" aria-live="polite"><strong>Pré-visualização</strong><p>{criarPrevia(configuracao)}</p></div>
          <p className="painel-lembrete-aviso">O lembrete só é enviado para clientes com atendimento marcado como Concluído. Faltas e cancelamentos não geram envio.</p>
          {erro && <p role="alert" className="painel-lembrete-erro">{erro}</p>}
          {sucesso && <p role="status" className="painel-lembrete-sucesso">{sucesso}</p>}
          <button type="submit" disabled={salvando || !alterada} className={classeBotaoPrimario}>{salvando ? "Salvando..." : "Salvar"}</button>
        </form>

        <aside className="painel-lembrete-relatorio" aria-labelledby="titulo-relatorio-lembrete">
          <div><h3 id="titulo-relatorio-lembrete">Últimos 30 dias</h3><p>Resultados atualizados pelo sistema a cada 15 minutos.</p></div>
          {carregandoRelatorio && <p className="painel-lembrete-carregando">Carregando resultados...</p>}
          {!carregandoRelatorio && erroRelatorio && <div className="painel-lembrete-relatorio-erro"><p role="alert">{erroRelatorio}</p><button type="button" className={classeBotaoSecundario} onClick={carregarRelatorio}>Tentar novamente</button></div>}
          {!carregandoRelatorio && !erroRelatorio && relatorio && relatorio.enviados === 0 && <p className="painel-lembrete-vazio">Nenhum lembrete enviado ainda</p>}
          {!carregandoRelatorio && !erroRelatorio && relatorio && relatorio.enviados > 0 && <dl className="painel-lembrete-indicadores"><div><dt>Enviados</dt><dd>{relatorio.enviados}</dd></div><div><dt>Geraram novo agendamento em até 7 dias</dt><dd>{relatorio.geraramAgendamento}</dd></div></dl>}
        </aside>
      </div>}
    </section>
  );
}
