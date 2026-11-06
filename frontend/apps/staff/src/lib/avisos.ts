/**
 * Envejecimiento de los avisos de mesa (llamar al camarero, PIN, cuenta, "¿cuándo sale?").
 *
 * Vive aquí, y no en cada pantalla, porque **el PDA y el Salón tienen que coincidir**:
 * un aviso que en una está en rojo no puede estar en ámbar en la otra. Con los
 * umbrales copiados en dos sitios, el día que alguien afine uno se separan sin que
 * falle nada — y nadie se entera hasta que dos camareros discuten.
 */

/** A partir de aquí el aviso deja de ser "recién llegado". */
export const ESPERA_S = 60;
/** A partir de aquí ya no es un aviso: es una queja esperando a ocurrir. */
export const URGENTE_S = 180;

export type NivelAviso = "nuevo" | "espera" | "urgente";

export function nivelDe(segundos: number): NivelAviso {
  if (segundos >= URGENTE_S) return "urgente";
  if (segundos >= ESPERA_S) return "espera";
  return "nuevo";
}

/** Segundos que lleva esperando un aviso, contra el reloj que pase la pantalla. */
export function segundosDesde(createdAt: string, ahora: number): number {
  return Math.max(0, Math.floor((ahora - new Date(createdAt).getTime()) / 1000));
}

/** Por debajo del minuto se cuenta en segundos: el primer minuto es el que importa. */
export function desdeHace(segundos: number): string {
  if (segundos < 60) return `${segundos}s`;
  const m = Math.floor(segundos / 60);
  return `${m} min`;
}

/** Aviso sonoro. `suave` para lo que no corre prisa (la pregunta "¿cuándo sale?"). */
export function pitido(tono: "aviso" | "suave" = "aviso") {
  if (typeof window === "undefined") return;
  const suave = tono === "suave";
  try {
    const ctx = new window.AudioContext();
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.frequency.value = suave ? 587 : 880;
    osc.start();
    gain.gain.setValueAtTime(suave ? 0.08 : 0.15, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + (suave ? 0.35 : 0.2));
    osc.stop(ctx.currentTime + (suave ? 0.35 : 0.2));
  } catch {
    /* el navegador puede no dejar sonar hasta que haya un toque en pantalla */
  }
}
