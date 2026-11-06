"use client";

import { useCallback, useEffect, useState } from "react";

/**
 * Modo tablero: nació en el KDS —la pantalla de cocina cuelga de la pared y cada píxel cuenta—
 * y hoy lo comparten todos los módulos del staff (caja, recepción, PDA, runner, salón e
 * historial) a través de `<FullscreenButton />`. La vista toma TODA la pantalla: sin la barra
 * lateral de la app y, si el navegador deja, sin su propia interfaz (fullscreen nativo).
 *
 * Son dos cosas que viajan juntas pero fallan por separado: ocultar la barra siempre funciona;
 * el fullscreen nativo puede negarse (navegador, permisos, iframe). Por eso el estado que manda
 * es el NUESTRO —`kiosk`— y el fullscreen es un extra que se intenta y, si sale, se acompaña.
 *
 * Se recuerda entre recargas para la pantalla fija de cocina: al volver, la barra sigue oculta.
 * El fullscreen nativo NO se puede reactivar solo (los navegadores exigen un gesto del usuario),
 * así que al recargar el tablero queda a pantalla completa "de app" y basta un clic para el resto.
 */

const KEY = "kds_kiosk";

/** Evento propio: el store es un módulo, no un contexto, y varios componentes lo observan. */
const EVENT = "kds-kiosk-change";

function leerGuardado(): boolean {
  if (typeof window === "undefined") return false;
  try {
    return window.localStorage.getItem(KEY) === "1";
  } catch {
    return false;
  }
}

function emitir(valor: boolean) {
  if (typeof window === "undefined") return;
  window.dispatchEvent(new CustomEvent<boolean>(EVENT, { detail: valor }));
}

function guardar(valor: boolean) {
  try {
    window.localStorage.setItem(KEY, valor ? "1" : "0");
  } catch {
    // Modo privado o almacenamiento lleno: el modo sigue funcionando, solo no se recuerda.
  }
}

/**
 * Observa el modo tablero. Lo usan tanto el KDS (que lo enciende) como el shell (que se aparta).
 * Arranca en `false` en el primer render para que servidor y cliente pinten lo mismo, y se
 * hidrata con lo guardado en un efecto — si no, React se queja de que el HTML no coincide.
 */
export function useKioskMode() {
  const [kiosk, setKiosk] = useState(false);

  useEffect(() => {
    setKiosk(leerGuardado());
    const onChange = (e: Event) => setKiosk((e as CustomEvent<boolean>).detail);
    const onFsChange = () => {
      // Salir del fullscreen con Esc (o con el gesto del navegador) también sale del modo
      // tablero: el encargado hizo el gesto de "devuélveme la pantalla", no medio gesto.
      if (!document.fullscreenElement && leerGuardado()) {
        guardar(false);
        setKiosk(false);
        // Y se avisa al resto de instancias del hook: en una vista hay al menos dos (el shell y
        // el botón de la cabecera) y todas escuchan `fullscreenchange`. La primera que atiende
        // apaga el guardado, así que las siguientes ven ya un "0", fallan este `if` y se
        // quedarían encendidas — la barra sin volver y el botón diciendo lo contrario.
        emitir(false);
      }
    };
    window.addEventListener(EVENT, onChange);
    document.addEventListener("fullscreenchange", onFsChange);
    return () => {
      window.removeEventListener(EVENT, onChange);
      document.removeEventListener("fullscreenchange", onFsChange);
    };
  }, []);

  return kiosk;
}

/**
 * Enciende y apaga el modo tablero. Devuelve el estado y la palanca; el fullscreen nativo se
 * intenta pero nunca bloquea: si el navegador lo niega, la barra se oculta igual.
 */
export function useKioskToggle(): [boolean, () => void] {
  const kiosk = useKioskMode();

  const toggle = useCallback(() => {
    const siguiente = !kiosk;
    guardar(siguiente);
    emitir(siguiente);

    // El fullscreen nativo es el extra. Se pide en el mismo gesto del clic (los navegadores solo
    // lo conceden así) y se ignoran los fallos a propósito.
    try {
      if (siguiente) {
        void document.documentElement.requestFullscreen?.().catch(() => {});
      } else if (document.fullscreenElement) {
        void document.exitFullscreen?.().catch(() => {});
      }
    } catch {
      // Navegador sin soporte: nos quedamos con el modo tablero "de app".
    }
  }, [kiosk]);

  return [kiosk, toggle];
}
