"use client";

import { useEffect, useState } from "react";
import { API_OFFLINE_EVENT, API_ONLINE_EVENT } from "@mestobeach/api-client";

/**
 * Aviso de que no se puede hablar con el servidor.
 *
 * Sin esto, un fallo de red deja la pantalla en «cargando» indefinidamente y sin
 * explicación: pasó de verdad cuando cambió la IP del equipo y las apps seguían
 * llamando a la dirección vieja. Un error del servidor (4xx/5xx) sí trae mensaje
 * y lo enseña cada pantalla; lo que no se veía era la ausencia de respuesta.
 *
 * Es una barra de estado, no un aviso puntual: la condición dura mientras dure, y
 * desaparece sola en cuanto una petición vuelve a responder.
 */
export default function ConnectionBanner() {
  const [caido, setCaido] = useState(false);

  useEffect(() => {
    const alCaer = () => setCaido(true);
    const alVolver = () => setCaido(false);
    window.addEventListener(API_OFFLINE_EVENT, alCaer);
    window.addEventListener(API_ONLINE_EVENT, alVolver);
    // El navegador también avisa de que se quedó sin red, antes incluso de que
    // falle ninguna petición.
    window.addEventListener("offline", alCaer);
    return () => {
      window.removeEventListener(API_OFFLINE_EVENT, alCaer);
      window.removeEventListener(API_ONLINE_EVENT, alVolver);
      window.removeEventListener("offline", alCaer);
    };
  }, []);

  if (!caido) return null;

  return (
    <div
      role="alert"
      aria-live="assertive"
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        zIndex: 2000,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        gap: "0.6rem",
        padding: "0.7rem 1rem",
        background: "#8a2c22",
        color: "#fff",
        fontSize: "0.9rem",
        lineHeight: 1.35,
        textAlign: "center",
        boxShadow: "0 2px 12px rgba(0,0,0,0.25)",
        animation: "mestobeach-cb-entra 240ms ease-out both",
      }}
    >
      <style>{`
        @keyframes mestobeach-cb-entra {
          from { transform: translateY(-100%); }
          to   { transform: translateY(0); }
        }
      `}</style>
      <span
        aria-hidden
        style={{
          width: 8,
          height: 8,
          borderRadius: "50%",
          background: "#ffd7d1",
          flexShrink: 0,
        }}
      />
      <span>
        <strong style={{ fontWeight: 700 }}>Sin conexión con el servidor.</strong>{" "}
        No se han podido cargar los datos. Comprueba la conexión y vuelve a intentarlo.
      </span>
    </div>
  );
}
