/** Conversão exclusiva da gestão pública; não altera os helpers do assistente. */
export function formatarHorarioEstabelecimento(iso: string, fuso: string): string {
  if (!fuso) throw new Error("Fuso do estabelecimento indisponível.");
  return new Intl.DateTimeFormat("pt-BR", {
    timeZone: fuso, dateStyle: "full", timeStyle: "short",
  }).format(new Date(iso));
}

function instanteCivil(ano: number, mes: number, dia: number, hora: number, minuto: number, segundo = 0): number {
  const data = new Date(0);
  data.setUTCFullYear(ano, mes - 1, dia);
  data.setUTCHours(hora, minuto, segundo, 0);
  return data.getTime();
}

/**
 * O datetime-local é um relógio de parede do negócio, nunca do navegador.
 * Descobre os offsets IANA na janela de instantes possíveis e verifica cada
 * candidato por conversão inversa. Inclui DST e offsets fracionários/históricos.
 * Recusa gaps e folds: não escolhe silenciosamente entre dois instantes.
 */
export function horarioEstabelecimentoParaIso(valor: string, fuso: string): string {
  if (!fuso) throw new Error("Fuso do estabelecimento indisponível.");
  const campos = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(valor);
  if (!campos) throw new Error("Informe uma data e um horário válidos.");
  const [ano, mes, dia, hora, minuto, segundo] = campos.slice(1).map(n => Number(n ?? 0));
  const civil = instanteCivil(ano, mes, dia, hora, minuto, segundo);
  const data = new Date(civil);
  if (ano < 1 || data.getUTCFullYear() !== ano || data.getUTCMonth() + 1 !== mes ||
      data.getUTCDate() !== dia || data.getUTCHours() !== hora ||
      data.getUTCMinutes() !== minuto || data.getUTCSeconds() !== segundo) {
    throw new Error("Informe uma data e um horário válidos.");
  }
  const formato = new Intl.DateTimeFormat("en-GB", {
    timeZone: fuso, calendar: "iso8601", numberingSystem: "latn", hourCycle: "h23",
    year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit",
  });
  const relogio = (instante: number) => {
    const partes = formato.formatToParts(instante);
    const parte = (tipo: string) => Number(partes.find(p => p.type === tipo)!.value);
    return instanteCivil(parte("year"), parte("month"), parte("day"), parte("hour"), parte("minute"), parte("second"));
  };
  // A janela cobre offsets IANA até ±24h. A resolução do controle é de minutos;
  // os offsets são calculados em segundos, sem arredondamento para horas/minutos.
  const offsets = new Set<number>();
  for (let minutos = -1440; minutos <= 1440; minutos++) {
    const instante = civil + minutos * 60_000;
    offsets.add(relogio(instante) - instante);
  }
  const candidatos = [...offsets].map(offset => civil - offset).filter(instante => relogio(instante) === civil);
  if (candidatos.length === 0) {
    throw new Error("Esse horário não existe no fuso do estabelecimento devido à mudança de horário. Escolha outro horário.");
  }
  if (candidatos.length !== 1) {
    throw new Error("Esse horário ocorre duas vezes no fuso do estabelecimento devido à mudança de horário. Escolha outro horário.");
  }
  return new Date(candidatos[0]).toISOString();
}
