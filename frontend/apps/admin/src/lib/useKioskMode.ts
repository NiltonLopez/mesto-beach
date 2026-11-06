"use client";

import { useCallback, useEffect, useState } from "react";

/**
 * Pantalla completa del panel de administración: el dueño trabaja en mesas, catálogo o analítica
 * y a veces necesita TODO el ancho — sin la barra lateral de navegación y, si el navegador deja,
 * sin su propia interfaz (fullscreen nativo). El editor de mesas es el que más lo agradece.
 *
 * Son dos cosas que viajan juntas pero fallan por separado: ocultar la barra siempre funciona;
 * el fullscreen nativo puede negarse (navegador, permisos, iframe). Por eso el estado que manda
 * es el NUESTRO —`kiosk`— y el fullscreen es un extra que se intenta y, si sale, se acompaña.
 *
 * Se recuerda entre recargas. El fullscreen nativo NO se puede reactivar solo (los navegadores
 * exigen un gesto del usuario), así que al recargar el panel queda a pantalla completa "de app"
 * y basta un clic para el resto.
 *
 * NOTA (deuda consciente): este hook es un duplicado casi literal del de `apps/staff`
 * (`apps/staff/src/lib/useKioskMode.ts`), igual que ya pasa con otros componentes de este repo
 * (Dialog, StatCard…). Se acepta por ahora porque `packages/ui` sigue vacío y montar el paquete
 * compartido para un hook de 100 líneas costaría más que mantener las dos copias; si aparece una
 * tercera app que lo necesite, ese es el momento de extraerlo. Ojo al tocarlo: las dos copias
 * deben cambiar juntas.
 *
 * La CLAVE de almacenamiento es distinta a propósito (`admin_kiosk` vs `kds_kiosk`): son pantallas
 * y usos distintos —una es de pared en cocina, la otra el escritorio del dueño— y el modo de una
 * no debe encender el de la otra.
 */

const KEY = "admin_kiosk";

/** Evento propio: el store es un módulo, no un contexto, y varios componentes lo observan. */
const EVENT = "admin-kiosk-change";

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
 * Observa el modo pantalla completa. Lo usan tanto el botón (que lo enciende) como el layout
 * (que aparta la barra lateral).
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
      // pantalla completa: el usuario hizo el gesto de "devuélveme la pantalla", no medio gesto.
      if (!document.fullscreenElement && leerGuardado()) {
        guardar(false);
        setKiosk(false);
        // Y se avisa al resto de instancias del hook: aquí hay al menos dos (el layout y el
        // botón de la barra) y todas escuchan `fullscreenchange`. La primera que atiende apaga
        // el guardado, así que las siguientes ven ya un "0", fallan este `if` y se quedarían
        // encendidas — la barra lateral sin volver.
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
 * Enciende y apaga el modo pantalla completa. Devuelve el estado y la palanca; el fullscreen
 * nativo se intenta pero nunca bloquea: si el navegador lo niega, la barra se oculta igual.
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
      // Navegador sin soporte: nos quedamos con el modo pantalla completa "de app".
    }
  }, [kiosk]);

  return [kiosk, toggle];
}
