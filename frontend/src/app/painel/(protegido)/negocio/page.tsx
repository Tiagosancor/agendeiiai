"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import { classeBotaoPrimario, classeInput, classeLabel } from "@/components/estilos";
import { NOMES_DIAS_SEMANA, type HorarioFuncionamentoDia, type PerfilNegocio } from "@/lib/tipos";

function horarioPadrao(): HorarioFuncionamentoDia[] {
  return Array.from({ length: 7 }, (_, dia) => ({
    diaSemana: dia,
    abertura: dia === 0 ? null : "09:00",
    fechamento: dia === 0 ? null : "18:00",
    fechado: dia === 0,
  }));
}

export default function PaginaPerfilNegocio() {
  const { chamarApi } = useAutenticacao();
  const [perfil, setPerfil] = useState<PerfilNegocio | null>(null);
  const [horario, setHorario] = useState<HorarioFuncionamentoDia[]>(horarioPadrao());
  const [erro, setErro] = useState<string | null>(null);
  const [mensagemSucesso, setMensagemSucesso] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  const carregar = useCallback(async () => {
    try {
      const dados = await chamarApi<PerfilNegocio>("/painel/negocio");
      setPerfil(dados);
      setHorario(dados.horarioFuncionamento.length === 7 ? dados.horarioFuncionamento : horarioPadrao());
    } catch {
      setErro("Não foi possível carregar o perfil do negócio.");
    }
  }, [chamarApi]);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  function atualizarCampo<K extends keyof PerfilNegocio>(campo: K, valor: PerfilNegocio[K]) {
    setPerfil((atual) => (atual ? { ...atual, [campo]: valor } : atual));
  }

  function atualizarDia(diaSemana: number, alteracao: Partial<HorarioFuncionamentoDia>) {
    setHorario((atual) => atual.map((d) => (d.diaSemana === diaSemana ? { ...d, ...alteracao } : d)));
  }

  async function salvar(evento: FormEvent) {
    evento.preventDefault();
    if (!perfil) return;

    setErro(null);
    setMensagemSucesso(null);
    setSalvando(true);
    try {
      await chamarApi("/painel/negocio", {
        metodo: "PUT",
        corpo: {
          nomeExibido: perfil.nomeExibido,
          logoUrl: perfil.logoUrl || null,
          corPrimaria: perfil.corPrimaria || null,
          corSecundaria: perfil.corSecundaria || null,
          tituloPagina: perfil.tituloPagina || null,
          subtituloPagina: perfil.subtituloPagina || null,
          textoSobre: perfil.textoSobre || null,
          bairro: perfil.bairro || null,
          cidade: perfil.cidade || null,
          rua: perfil.rua || null,
          numero: perfil.numero || null,
          cep: perfil.cep || null,
          telefone: perfil.telefone || null,
          emailContato: perfil.emailContato || null,
          instagram: perfil.instagram || null,
          facebook: perfil.facebook || null,
          whatsApp: perfil.whatsApp || null,
          whatsAppAtivoParaConfirmacoes: perfil.whatsAppAtivoParaConfirmacoes,
          horarioFuncionamento: horario,
        },
      });
      setMensagemSucesso("Perfil atualizado.");
    } catch {
      setErro("Não foi possível salvar o perfil do negócio.");
    } finally {
      setSalvando(false);
    }
  }

  if (erro && !perfil) return <p className="text-sm text-red-600">{erro}</p>;
  if (!perfil) return <p className="text-sm text-gray-500 dark:text-neutral-400">Carregando...</p>;

  return (
    <form onSubmit={salvar} className="max-w-2xl space-y-8">
      <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Meu negócio</h1>

      <section className="space-y-3">
        <h2 className="text-sm font-semibold tracking-wide text-gray-500 uppercase dark:text-neutral-400">Marca</h2>
        <label>
          <span className={classeLabel}>Nome exibido</span>
          <input required className={classeInput} value={perfil.nomeExibido} onChange={(e) => atualizarCampo("nomeExibido", e.target.value)} />
        </label>
        <div className="flex gap-3">
          <label className="flex-1">
            <span className={classeLabel}>Cor primária</span>
            <input className={classeInput} value={perfil.corPrimaria ?? ""} onChange={(e) => atualizarCampo("corPrimaria", e.target.value)} placeholder="#000000" />
          </label>
          <label className="flex-1">
            <span className={classeLabel}>Cor secundária</span>
            <input className={classeInput} value={perfil.corSecundaria ?? ""} onChange={(e) => atualizarCampo("corSecundaria", e.target.value)} placeholder="#FFFFFF" />
          </label>
        </div>
        <label>
          <span className={classeLabel}>URL do logo</span>
          <input className={classeInput} value={perfil.logoUrl ?? ""} onChange={(e) => atualizarCampo("logoUrl", e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Título da página inicial</span>
          <input className={classeInput} value={perfil.tituloPagina ?? ""} onChange={(e) => atualizarCampo("tituloPagina", e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Subtítulo da página inicial</span>
          <input className={classeInput} value={perfil.subtituloPagina ?? ""} onChange={(e) => atualizarCampo("subtituloPagina", e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Texto &quot;sobre&quot;</span>
          <textarea className={classeInput} rows={3} value={perfil.textoSobre ?? ""} onChange={(e) => atualizarCampo("textoSobre", e.target.value)} />
        </label>
      </section>

      <section className="space-y-3">
        <h2 className="text-sm font-semibold tracking-wide text-gray-500 uppercase dark:text-neutral-400">Endereço e contato</h2>
        <div className="flex gap-3">
          <label className="flex-1">
            <span className={classeLabel}>Bairro</span>
            <input className={classeInput} value={perfil.bairro ?? ""} onChange={(e) => atualizarCampo("bairro", e.target.value)} />
          </label>
          <label className="flex-1">
            <span className={classeLabel}>Cidade</span>
            <input className={classeInput} value={perfil.cidade ?? ""} onChange={(e) => atualizarCampo("cidade", e.target.value)} />
          </label>
        </div>
        <div className="flex gap-3">
          <label className="flex-[2]">
            <span className={classeLabel}>Rua</span>
            <input className={classeInput} value={perfil.rua ?? ""} onChange={(e) => atualizarCampo("rua", e.target.value)} />
          </label>
          <label className="flex-1">
            <span className={classeLabel}>Número</span>
            <input className={classeInput} value={perfil.numero ?? ""} onChange={(e) => atualizarCampo("numero", e.target.value)} />
          </label>
          <label className="flex-1">
            <span className={classeLabel}>CEP</span>
            <input className={classeInput} value={perfil.cep ?? ""} onChange={(e) => atualizarCampo("cep", e.target.value)} />
          </label>
        </div>
        <div className="flex gap-3">
          <label className="flex-1">
            <span className={classeLabel}>Telefone</span>
            <input className={classeInput} value={perfil.telefone ?? ""} onChange={(e) => atualizarCampo("telefone", e.target.value)} />
          </label>
          <label className="flex-1">
            <span className={classeLabel}>E-mail de contato (Fale Conosco)</span>
            <input
              type="email"
              className={classeInput}
              value={perfil.emailContato ?? ""}
              onChange={(e) => atualizarCampo("emailContato", e.target.value)}
            />
          </label>
        </div>
        <div className="flex gap-3">
          <label className="flex-1">
            <span className={classeLabel}>Instagram</span>
            <input className={classeInput} value={perfil.instagram ?? ""} onChange={(e) => atualizarCampo("instagram", e.target.value)} />
          </label>
          <label className="flex-1">
            <span className={classeLabel}>Facebook</span>
            <input className={classeInput} value={perfil.facebook ?? ""} onChange={(e) => atualizarCampo("facebook", e.target.value)} />
          </label>
          <label className="flex-1">
            <span className={classeLabel}>WhatsApp</span>
            <input className={classeInput} value={perfil.whatsApp ?? ""} onChange={(e) => atualizarCampo("whatsApp", e.target.value)} />
          </label>
        </div>
        <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-neutral-300">
          <input
            type="checkbox"
            checked={perfil.whatsAppAtivoParaConfirmacoes}
            onChange={(e) => atualizarCampo("whatsAppAtivoParaConfirmacoes", e.target.checked)}
          />
          Enviar confirmações e lembretes também por WhatsApp (além do código, que é sempre enviado)
        </label>
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-semibold tracking-wide text-gray-500 uppercase dark:text-neutral-400">Horário de funcionamento</h2>
        {horario.map((dia) => (
          <div key={dia.diaSemana} className="flex flex-wrap items-center gap-3 text-sm">
            <span className="w-24 text-gray-700 dark:text-neutral-200">{NOMES_DIAS_SEMANA[dia.diaSemana]}</span>
            <label className="flex items-center gap-1">
              <input
                type="checkbox"
                checked={dia.fechado}
                onChange={(e) => atualizarDia(dia.diaSemana, { fechado: e.target.checked })}
              />
              Fechado
            </label>
            {!dia.fechado && (
              <>
                <input
                  type="time"
                  className={`${classeInput} w-32`}
                  value={dia.abertura ?? ""}
                  onChange={(e) => atualizarDia(dia.diaSemana, { abertura: e.target.value })}
                />
                <span>até</span>
                <input
                  type="time"
                  className={`${classeInput} w-32`}
                  value={dia.fechamento ?? ""}
                  onChange={(e) => atualizarDia(dia.diaSemana, { fechamento: e.target.value })}
                />
              </>
            )}
          </div>
        ))}
      </section>

      {erro && <p className="text-sm text-red-600">{erro}</p>}
      {mensagemSucesso && <p className="text-sm text-green-600">{mensagemSucesso}</p>}

      <button type="submit" disabled={salvando} className={classeBotaoPrimario}>
        {salvando ? "Salvando..." : "Salvar alterações"}
      </button>
    </form>
  );
}
