/** Datas civis e horários do estabelecimento, independentemente do fuso do dispositivo. */
export function dataNoFuso(instante: Date | string, fuso: string): string {
  const partes = new Intl.DateTimeFormat("en-CA", {
    timeZone: fuso, year: "numeric", month: "2-digit", day: "2-digit",
  }).formatToParts(new Date(instante));
  const parte = (tipo: string) => partes.find((p) => p.type === tipo)!.value;
  return `${parte("year")}-${parte("month")}-${parte("day")}`;
}

export function proximosDiasDoNegocio(quantidade: number, fuso: string): string[] {
  const hoje = dataNoFuso(new Date(), fuso);
  return Array.from({ length: quantidade }, (_, i) => {
    const dia = new Date(`${hoje}T12:00:00Z`);
    dia.setUTCDate(dia.getUTCDate() + i);
    return dia.toISOString().slice(0, 10);
  });
}

export function formatarDiaDaFaixa(dia: string): string {
  return new Date(`${dia}T12:00:00Z`).toLocaleDateString("pt-BR", { timeZone: "UTC", weekday: "short" });
}

export function formatarHoraDoNegocio(iso: string, fuso: string): string {
  return new Date(iso).toLocaleTimeString("pt-BR", { timeZone: fuso, hour: "2-digit", minute: "2-digit" });
}

export function formatarDataDoNegocio(iso: string, fuso: string): string {
  return new Date(iso).toLocaleDateString("pt-BR", {
    timeZone: fuso, weekday: "long", day: "2-digit", month: "long",
  });
}
