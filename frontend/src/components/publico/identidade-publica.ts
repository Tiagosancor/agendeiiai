import type { CSSProperties } from "react";

/** Mantém a cor do negócio e escolhe apenas a cor de texto sobre ela. */
export function identidadePublica(corPrimaria: string | null, corSecundaria: string | null): CSSProperties {
  const acento = /^#[\da-f]{6}$/i.test(corPrimaria ?? "") ? corPrimaria! : "#2563eb";
  const canais = [1, 3, 5].map((inicio) => {
    const canal = parseInt(acento.slice(inicio, inicio + 2), 16) / 255;
    return canal <= .04045 ? canal / 12.92 : ((canal + .055) / 1.055) ** 2.4;
  });
  const luminancia = canais[0] * .2126 + canais[1] * .7152 + canais[2] * .0722;
  const contrasteBranco = 1.05 / (luminancia + .05);
  const contrasteEscuro = (luminancia + .05) / .05;
  return {
    "--cor-primaria": acento,
    "--cor-secundaria": corSecundaria ?? "#1d4ed8",
    "--publico-sobre-acento": contrasteBranco >= contrasteEscuro ? "#ffffff" : "#000000",
  } as CSSProperties;
}
