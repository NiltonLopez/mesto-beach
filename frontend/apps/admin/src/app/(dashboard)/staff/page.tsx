"use client";

import { Fragment, useEffect, useState } from "react";
import { MapPin, Monitor, Warning } from "@phosphor-icons/react";
import type { FloorDto, KdsStationDto, StaffMemberDto } from "@mestobeach/api-client";
import { kdsApi, sessionsApi, staffMgmtApi } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
import { useDialog } from "@/components/Dialog";

/**
 * Roles con los que trabaja Mesto Beach (SDD-001 v2.0 — PRD-018). El orden es el de la sala:
 * quien manda en la puerta, quien comanda, quien reparte, y al final cocina y los heredados.
 */
const ROLES: { value: string; hint: string }[] = [
  { value: "Admin", hint: "Gestiona el restaurante entero" },
  { value: "Cajero", hint: "Cobra y cierra cuentas" },
  { value: "Host", hint: "Sienta a la gente; ve todos los sectores" },
  { value: "PDA", hint: "Comanda y marcha platos en su sector" },
  { value: "Runner", hint: "Arma bandeja y entrega desde su estación" },
  { value: "KDS", hint: "Pantalla de cocina o barra" },
  { value: "Mesero", hint: "Alias heredado de PDA" },
];

/**
 * Qué asignación exige cada rol. Se mira exactamente el rol que mira el backend
 * (`UserMembership.RequiresFloor` / `RequiresStation`): `Mesero` es el alias heredado de `PDA`,
 * pero **no** exige sector, así que aquí tampoco — bloquear el guardado de algo que la API
 * aceptaría sería inventarse una regla.
 */
function needsFloor(roles: string[]): boolean {
  return roles.includes("PDA");
}

function needsStation(roles: string[]): boolean {
  return roles.includes("Runner");
}

const STATUS_COLORS: Record<string, string> = {
  ACTIVE: "bg-oro/[0.10] text-brand-700",
  PENDING: "bg-amber-500/10 text-amber-700",
  INACTIVE: "bg-paper2 text-muted",
};

const STATUS_LABEL: Record<string, string> = {
  ACTIVE: "Activo",
  PENDING: "Pendiente",
  INACTIVE: "Inactivo",
};

type AccessMode = "ACTIVATION_LINK" | "DIRECT_ACCESS";

interface InviteResult {
  userId: string;
  staffName: string;
  email: string;
  kind?: "activation" | "reset";
  activationUrl?: string;
  expiresAt?: string;
  temporaryPassword?: string;
}

/** El backend devuelve ProblemDetails con el mensaje ya en español dentro de `detail`. */
function detalleDeError(err: unknown, respaldo: string): string {
  return (
    (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? respaldo
  );
}

export default function StaffPage() {
  const { staff } = useAuthStore();
  const dialog = useDialog();
  const isAdmin = staff?.roles?.includes("Admin") ?? false;

  const [inviting, setInviting] = useState(false);
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [roles, setRoles] = useState<string[]>(["PDA"]);
  const [floorId, setFloorId] = useState("");
  const [stationId, setStationId] = useState("");
  const [accessMode, setAccessMode] = useState<AccessMode>("ACTIVATION_LINK");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const [staffList, setStaffList] = useState<StaffMemberDto[]>([]);
  const [listLoading, setListLoading] = useState(true);

  // Sectores y estaciones que ya existen: son los que llenan los desplegables de asignación.
  const [floors, setFloors] = useState<FloorDto[]>([]);
  const [stations, setStations] = useState<KdsStationDto[]>([]);

  const [banner, setBanner] = useState<InviteResult | null>(null);
  const [rowError, setRowError] = useState<Record<string, string>>({});
  const [copied, setCopied] = useState(false);

  // Editor en línea de roles + asignación
  const [editingUserId, setEditingUserId] = useState<string | null>(null);
  const [editRoles, setEditRoles] = useState<string[]>([]);
  const [editFloorId, setEditFloorId] = useState("");
  const [editStationId, setEditStationId] = useState("");
  const [savingRow, setSavingRow] = useState(false);

  useEffect(() => {
    staffMgmtApi
      .listStaff()
      .then((r) => setStaffList(r.data))
      .catch(() => setStaffList([]))
      .finally(() => setListLoading(false));
  }, []);

  useEffect(() => {
    sessionsApi
      .listFloors()
      .then((r) => setFloors(r.data))
      .catch(() => setFloors([]));
    kdsApi
      .listStations()
      .then((r) => setStations(r.data.data))
      .catch(() => setStations([]));
  }, []);

  const floorName = (id?: string | null): string | null =>
    id ? (floors.find((f) => f.id === id)?.name ?? "Sector eliminado") : null;

  const stationName = (id?: string | null): string | null =>
    id ? (stations.find((s) => s.id === id)?.name ?? "Estación eliminada") : null;

  function toggle(list: string[], r: string): string[] {
    return list.includes(r) ? list.filter((x) => x !== r) : [...list, r];
  }

  /**
   * Comprueba que la asignación obligatoria esté elegida. Devuelve el mensaje que hay que
   * enseñar, o `null` si se puede guardar.
   */
  function validarAsignacion(
    seleccion: string[],
    sector: string,
    estacion: string
  ): string | null {
    if (needsFloor(seleccion) && !sector) {
      return floors.length === 0
        ? "El rol PDA necesita un sector asignado y todavía no hay ninguno. Crea los sectores en Mesas."
        : "El rol PDA necesita un sector asignado: elige en cuál atiende.";
    }
    if (needsStation(seleccion) && !estacion) {
      return stations.length === 0
        ? "El rol Runner necesita una estación asignada y todavía no hay ninguna. Créalas en Estaciones KDS."
        : "El rol Runner necesita una estación asignada: elige de cuál reparte.";
    }
    return null;
  }

  async function handleInvite(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSuccess(null);
    setBanner(null);
    if (roles.length === 0) {
      setError("Selecciona al menos un rol.");
      return;
    }
    const faltaAsignacion = validarAsignacion(roles, floorId, stationId);
    if (faltaAsignacion) {
      setError(faltaAsignacion);
      return;
    }
    setSubmitting(true);
    try {
      const { data } = await staffMgmtApi.inviteStaff({ name, email, roles, accessMode });

      // La invitación no lleva asignación: se fija justo después con su propio endpoint, que es
      // quien valida que el sector y la estación existan en este restaurante.
      let asignadoFloorId: string | null = null;
      let asignadoStationId: string | null = null;
      let avisoAsignacion = "";
      if (needsFloor(roles) || needsStation(roles)) {
        try {
          const res = await staffMgmtApi.updateStaffAssignment(data.userId, {
            assignedFloorId: needsFloor(roles) ? floorId : null,
            assignedStationId: needsStation(roles) ? stationId : null,
          });
          asignadoFloorId = res.data.assignedFloorId;
          asignadoStationId = res.data.assignedStationId;
        } catch (err: unknown) {
          avisoAsignacion = `Se creó la cuenta, pero no se pudo guardar su asignación: ${detalleDeError(
            err,
            "error inesperado."
          )} Edítala desde esta lista.`;
        }
      }

      const memberDto: StaffMemberDto = {
        userId: data.userId,
        name: data.name,
        email: data.email,
        roles: data.roles,
        status: data.status,
        tenantId: data.tenantId,
        createdAt: data.createdAt,
        assignedFloorId: asignadoFloorId,
        assignedStationId: asignadoStationId,
      };
      // Replace if the email already existed in this tenant, else prepend.
      setStaffList((prev) => {
        const without = prev.filter((p) => p.userId !== data.userId);
        return [memberDto, ...without];
      });
      setRowError((prev) => ({ ...prev, [data.userId]: avisoAsignacion }));

      if (data.accessMode === "EXISTING_USER") {
        setSuccess(`"${data.name}" ya tenía cuenta — se le concedió acceso con su contraseña actual.`);
      } else {
        setBanner({
          userId: data.userId,
          staffName: data.name,
          email: data.email,
          ...(data.activationUrl ? { activationUrl: data.activationUrl } : {}),
          ...(data.temporaryPassword ? { temporaryPassword: data.temporaryPassword } : {}),
        });
        setSuccess(`Colaborador "${data.name}" agregado.`);
      }
      setName("");
      setEmail("");
      setRoles(["PDA"]);
      setFloorId("");
      setStationId("");
      setAccessMode("ACTIVATION_LINK");
      setInviting(false);
    } catch (err: unknown) {
      setError(detalleDeError(err, "No se pudo enviar la invitación."));
    } finally {
      setSubmitting(false);
    }
  }

  async function handleResend(s: StaffMemberDto) {
    setRowError((prev) => ({ ...prev, [s.userId]: "" }));
    setBanner(null);
    try {
      const { data } = await staffMgmtApi.resendInvitation(s.userId);
      setBanner({
        userId: s.userId, staffName: s.name, email: s.email,
        activationUrl: data.activationUrl, expiresAt: data.expiresAt,
      });
    } catch (err: unknown) {
      setRowError((prev) => ({
        ...prev,
        [s.userId]: detalleDeError(err, "No se pudo reenviar la invitación."),
      }));
    }
  }

  async function handleResetPassword(s: StaffMemberDto) {
    setRowError((prev) => ({ ...prev, [s.userId]: "" }));
    setBanner(null);
    try {
      const { data } = await staffMgmtApi.resetStaffPassword(s.userId);
      setBanner({
        userId: s.userId, staffName: s.name, email: s.email, kind: "reset",
        activationUrl: data.resetUrl, expiresAt: data.expiresAt,
      });
    } catch (err: unknown) {
      setRowError((prev) => ({
        ...prev,
        [s.userId]: detalleDeError(err, "No se pudo generar el enlace de restablecimiento."),
      }));
    }
  }

  function startEditing(s: StaffMemberDto) {
    setEditingUserId(s.userId);
    setEditRoles(s.roles);
    setEditFloorId(s.assignedFloorId ?? "");
    setEditStationId(s.assignedStationId ?? "");
    setRowError((prev) => ({ ...prev, [s.userId]: "" }));
  }

  /**
   * Guarda roles y asignación. **El orden importa**: el endpoint de asignación decide qué es
   * obligatorio mirando los roles ya persistidos, así que los roles van primero. Además, quitar
   * un rol descarta en el backend la asignación que ya no exige, por eso se compara contra lo
   * que devuelve el PATCH de roles y no contra lo que había en pantalla.
   */
  async function handleSaveMember(s: StaffMemberDto) {
    setRowError((prev) => ({ ...prev, [s.userId]: "" }));
    if (editRoles.length === 0) {
      setRowError((prev) => ({ ...prev, [s.userId]: "Selecciona al menos un rol." }));
      return;
    }
    const faltaAsignacion = validarAsignacion(editRoles, editFloorId, editStationId);
    if (faltaAsignacion) {
      setRowError((prev) => ({ ...prev, [s.userId]: faltaAsignacion }));
      return;
    }

    setSavingRow(true);
    let rolesGuardados = false;
    try {
      const { data } = await staffMgmtApi.updateStaffRoles(s.userId, editRoles);
      rolesGuardados = true;
      setStaffList((prev) =>
        prev.map((p) =>
          p.userId === s.userId
            ? {
                ...p,
                roles: data.roles,
                assignedFloorId: data.assignedFloorId ?? null,
                assignedStationId: data.assignedStationId ?? null,
              }
            : p
        )
      );

      const deseadoFloorId = needsFloor(editRoles) ? editFloorId : null;
      const deseadoStationId = needsStation(editRoles) ? editStationId : null;
      const yaEstaba =
        (data.assignedFloorId ?? null) === deseadoFloorId &&
        (data.assignedStationId ?? null) === deseadoStationId;

      // Reasignar a lo mismo no se envía: cambiar de estación con una bandeja reclamada da 409,
      // y no tiene sentido arriesgar ese error cuando no hay nada que cambiar.
      if (!yaEstaba) {
        const res = await staffMgmtApi.updateStaffAssignment(s.userId, {
          assignedFloorId: deseadoFloorId,
          assignedStationId: deseadoStationId,
        });
        setStaffList((prev) =>
          prev.map((p) =>
            p.userId === s.userId
              ? {
                  ...p,
                  roles: res.data.roles,
                  assignedFloorId: res.data.assignedFloorId,
                  assignedStationId: res.data.assignedStationId,
                }
              : p
          )
        );
      }
      setEditingUserId(null);
    } catch (err: unknown) {
      // Si los roles se guardaron y falló la asignación, el aviso lo dice: el estado a medias se
      // ve en la fila (queda marcada "Sin sector"/"Sin estación") y se puede reintentar.
      const respaldo = rolesGuardados
        ? "Se guardaron los roles, pero no la asignación."
        : "No se pudieron actualizar los roles.";
      setRowError((prev) => ({ ...prev, [s.userId]: detalleDeError(err, respaldo) }));
    } finally {
      setSavingRow(false);
    }
  }

  async function handleRemove(s: StaffMemberDto) {
    if (
      !(await dialog.confirm({
        message: `¿Quitar a "${s.name}" de este restaurante? Conservará su cuenta y acceso a otros restaurantes.`,
        tone: "danger",
        confirmText: "Quitar",
      }))
    )
      return;
    setRowError((prev) => ({ ...prev, [s.userId]: "" }));
    try {
      await staffMgmtApi.removeStaff(s.userId);
      setStaffList((prev) => prev.filter((p) => p.userId !== s.userId));
    } catch (err: unknown) {
      setRowError((prev) => ({
        ...prev,
        [s.userId]: detalleDeError(err, "No se pudo quitar al colaborador."),
      }));
    }
  }

  async function copyUrl(url: string) {
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(url);
      } else {
        const el = document.createElement("textarea");
        el.value = url;
        el.style.position = "fixed";
        el.style.opacity = "0";
        document.body.appendChild(el);
        el.select();
        document.execCommand("copy");
        document.body.removeChild(el);
      }
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopied(false);
    }
  }

  const colSpan = isAdmin ? 6 : 5;

  return (
    <div className="p-6">
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-display text-ink">Personal</h1>
        {isAdmin && !inviting && (
          <button onClick={() => { setInviting(true); setBanner(null); setSuccess(null); setError(null); }} className="btn-primary">
            + Invitar
          </button>
        )}
      </div>

      {/* Invite form */}
      {inviting && isAdmin && (
        <div className="card p-5 mb-6">
          <h2 className="mb-4 text-base font-display text-ink">Invitar nuevo colaborador</h2>
          {error && (
            <div className="mb-3 rounded-lg bg-rose-500/10 border border-rose-500/30 px-4 py-2 text-sm text-rose-600">
              {error}
            </div>
          )}
          <form onSubmit={handleInvite} className="space-y-4">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label className="label">Nombre</label>
                <input type="text" value={name} onChange={(e) => setName(e.target.value)}
                  className="input" placeholder="Juan Pérez" required />
              </div>
              <div>
                <label className="label">Correo electrónico</label>
                <input type="email" value={email} onChange={(e) => setEmail(e.target.value)}
                  className="input" placeholder="juan@mestobeach.es" required />
              </div>
            </div>
            <div>
              <label className="label">Roles (puedes elegir varios)</label>
              <div className="flex flex-wrap gap-2">
                {ROLES.map((r) => (
                  <label
                    key={r.value}
                    title={r.hint}
                    className={`flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-1.5 text-sm ${
                      roles.includes(r.value) ? "border-brand-500 bg-oro/[0.10] text-brand-700" : "border-hair/50 text-muted"
                    }`}
                  >
                    <input type="checkbox" checked={roles.includes(r.value)} onChange={() => setRoles((p) => toggle(p, r.value))} />
                    {r.value}
                  </label>
                ))}
              </div>
              <p className="mt-1 text-xs text-muted">
                Si el correo ya tiene cuenta, se le da acceso a este restaurante con su contraseña actual.
              </p>
            </div>

            {/* Asignación: el PDA atiende un sector y el Runner sale de una estación */}
            {(needsFloor(roles) || needsStation(roles)) && (
              <div className="grid grid-cols-1 gap-4 rounded-lg border border-hair/50 bg-oro/[0.08] p-4 sm:grid-cols-2">
                {needsFloor(roles) && (
                  <div>
                    <label className="label flex items-center gap-1.5">
                      <MapPin size={16} weight="light" className="text-brand-700" aria-hidden />
                      Sector del PDA
                    </label>
                    {/* Sin `required`: la validación propia explica mejor el caso "aún no hay sectores". */}
                    <select className="input" value={floorId} onChange={(e) => setFloorId(e.target.value)}>
                      <option value="">Selecciona un sector…</option>
                      {floors.map((f) => (
                        <option key={f.id} value={f.id}>{f.name}</option>
                      ))}
                    </select>
                    <p className="mt-1 text-xs text-muted">
                      {floors.length === 0
                        ? "Todavía no hay sectores. Créalos en Mesas antes de dar el rol PDA."
                        : "Solo podrá comandar en las mesas de este sector."}
                    </p>
                  </div>
                )}
                {needsStation(roles) && (
                  <div>
                    <label className="label flex items-center gap-1.5">
                      <Monitor size={16} weight="light" className="text-brand-700" aria-hidden />
                      Estación del Runner
                    </label>
                    <select className="input" value={stationId} onChange={(e) => setStationId(e.target.value)}>
                      <option value="">Selecciona una estación…</option>
                      {stations.map((st) => (
                        <option key={st.id} value={st.id}>{st.name}</option>
                      ))}
                    </select>
                    <p className="mt-1 text-xs text-muted">
                      {stations.length === 0
                        ? "Todavía no hay estaciones. Créalas en Estaciones KDS antes de dar el rol Runner."
                        : "Verá la cola de esta estación y repartirá desde ella."}
                    </p>
                  </div>
                )}
              </div>
            )}

            <div>
              <label className="label">¿Cómo accede? (solo para cuentas nuevas)</label>
              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                <label className={`flex cursor-pointer flex-col gap-1 rounded-lg border px-3 py-2.5 text-sm ${accessMode === "DIRECT_ACCESS" ? "border-brand-500 bg-oro/[0.10]" : "border-hair/50 hover:border-hair"}`}>
                  <span className="flex items-center gap-2 font-medium text-ink">
                    <input type="radio" name="accessMode" checked={accessMode === "DIRECT_ACCESS"} onChange={() => setAccessMode("DIRECT_ACCESS")} />
                    Acceso directo
                  </span>
                  <span className="pl-6 text-xs text-muted">
                    Genera una contraseña temporal. La cuenta queda activa; la cambia en su primer ingreso.
                  </span>
                </label>
                <label className={`flex cursor-pointer flex-col gap-1 rounded-lg border px-3 py-2.5 text-sm ${accessMode === "ACTIVATION_LINK" ? "border-brand-500 bg-oro/[0.10]" : "border-hair/50 hover:border-hair"}`}>
                  <span className="flex items-center gap-2 font-medium text-ink">
                    <input type="radio" name="accessMode" checked={accessMode === "ACTIVATION_LINK"} onChange={() => setAccessMode("ACTIVATION_LINK")} />
                    Enlace de activación
                  </span>
                  <span className="pl-6 text-xs text-muted">
                    Genera un enlace (48h) para que el colaborador defina su propia contraseña.
                  </span>
                </label>
              </div>
            </div>
            <div className="flex gap-3">
              <button type="submit" disabled={submitting} className="btn-primary">
                {submitting ? "Creando…" : accessMode === "DIRECT_ACCESS" ? "Crear con acceso directo" : "Enviar invitación"}
              </button>
              <button type="button" onClick={() => setInviting(false)} className="btn-secondary">Cancelar</button>
            </div>
          </form>
        </div>
      )}

      {success && !banner && (
        <div className="mb-4 rounded-xl bg-emerald-500/10 border border-emerald-500/30 px-4 py-3 text-sm text-emerald-700">
          {success}
        </div>
      )}

      {/* Banner — temporary credentials */}
      {banner?.temporaryPassword && (
        <div className="mb-5 rounded-lg border border-emerald-500/30 bg-emerald-500/10 p-4 text-sm text-emerald-800">
          <p className="font-medium">Acceso directo para <span className="font-semibold">{banner.staffName}</span></p>
          <p className="mt-0.5 text-xs text-emerald-700">Comparte estas credenciales. Deberá cambiar la contraseña en su primer ingreso.</p>
          <dl className="mt-3 grid gap-1 font-mono text-xs">
            <div className="flex gap-2"><dt className="text-emerald-700">Correo:</dt><dd className="break-all">{banner.email}</dd></div>
            <div className="flex gap-2"><dt className="text-emerald-700">Contraseña temporal:</dt><dd className="break-all font-semibold">{banner.temporaryPassword}</dd></div>
          </dl>
          <div className="mt-3 flex items-center gap-3">
            <button onClick={() => void copyUrl(`Correo: ${banner.email}\nContraseña temporal: ${banner.temporaryPassword}`)}
              className="inline-flex rounded bg-emerald-700 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-800">
              {copied ? "¡Copiado!" : "Copiar credenciales"}
            </button>
            <button onClick={() => setBanner(null)} className="ml-auto text-xs text-emerald-600 hover:text-emerald-800">Cerrar</button>
          </div>
        </div>
      )}

      {/* Banner — activation/reset URL */}
      {banner?.activationUrl && (
        <div className="mb-5 rounded-lg border border-sky-500/30 bg-sky-500/10 p-4 text-sm text-sky-800">
          <p className="font-medium">
            {banner.kind === "reset" ? "Enlace de restablecimiento para" : "Enlace de activación para"}{" "}
            <span className="font-semibold">{banner.staffName}</span>
          </p>
          {banner.expiresAt && (
            <p className="mt-0.5 text-xs text-sky-700">Válido hasta {new Date(banner.expiresAt).toLocaleString("es-ES")}</p>
          )}
          <p className="mt-2 break-all font-mono text-xs">{banner.activationUrl}</p>
          <div className="mt-3 flex items-center gap-3">
            <button onClick={() => void copyUrl(banner.activationUrl!)}
              className="inline-flex rounded bg-sky-700 px-3 py-1.5 text-xs font-medium text-white hover:bg-sky-800">
              {copied ? "¡Copiado!" : "Copiar enlace"}
            </button>
            <a href={banner.activationUrl} target="_blank" rel="noreferrer" className="text-xs font-medium text-sky-700 hover:underline">Abrir</a>
            <button onClick={() => setBanner(null)} className="ml-auto text-xs text-sky-600 hover:text-sky-800">Cerrar</button>
          </div>
        </div>
      )}

      {/* Staff table */}
      {listLoading ? (
        <div className="flex justify-center py-12">
          <div className="h-6 w-6 animate-spin rounded-full border-4 border-hair/40 border-t-brand-500" />
        </div>
      ) : staffList.length > 0 ? (
        <div className="card overflow-hidden">
          <div className="border-b border-hair/40 px-5 py-3">
            <h2 className="text-base font-display text-ink">Equipo ({staffList.length})</h2>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-canvas border-b border-hair/40">
                <tr>
                  <th className="table-th">Nombre</th>
                  <th className="table-th">Correo</th>
                  <th className="table-th">Roles</th>
                  <th className="table-th">Asignación</th>
                  <th className="table-th">Estado</th>
                  {isAdmin && <th className="table-th">Acciones</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-hair/30">
                {staffList.map((s) => (
                  <Fragment key={s.userId}>
                    <tr className="hover:bg-canvas">
                      <td className="table-td font-medium">{s.name}</td>
                      <td className="table-td text-muted">{s.email}</td>
                      <td className="table-td">
                        {editingUserId === s.userId ? (
                          <div className="flex flex-wrap gap-1.5">
                            {ROLES.map((r) => (
                              <label key={r.value} title={r.hint} className={`flex cursor-pointer items-center gap-1 rounded border px-1.5 py-0.5 text-xs ${editRoles.includes(r.value) ? "border-brand-500 bg-oro/[0.10] text-brand-700" : "border-hair/50 text-muted"}`}>
                                <input type="checkbox" checked={editRoles.includes(r.value)} onChange={() => setEditRoles((p) => toggle(p, r.value))} />
                                {r.value}
                              </label>
                            ))}
                          </div>
                        ) : (
                          <div className="flex flex-wrap gap-1">
                            {s.roles.map((r) => (
                              <span key={r} className="rounded-full border border-hair/50 bg-paper2 px-2 py-0.5 text-xs font-medium tracking-[0.03em] text-ink">{r}</span>
                            ))}
                          </div>
                        )}
                      </td>
                      <td className="table-td">
                        {editingUserId === s.userId ? (
                          needsFloor(editRoles) || needsStation(editRoles) ? (
                            <div className="flex min-w-[12rem] flex-col gap-2">
                              {needsFloor(editRoles) && (
                                <select
                                  className="input py-1.5 text-xs"
                                  value={editFloorId}
                                  onChange={(e) => setEditFloorId(e.target.value)}
                                  aria-label="Sector del PDA"
                                >
                                  <option value="">Sector del PDA…</option>
                                  {floors.map((f) => (
                                    <option key={f.id} value={f.id}>{f.name}</option>
                                  ))}
                                </select>
                              )}
                              {needsStation(editRoles) && (
                                <select
                                  className="input py-1.5 text-xs"
                                  value={editStationId}
                                  onChange={(e) => setEditStationId(e.target.value)}
                                  aria-label="Estación del Runner"
                                >
                                  <option value="">Estación del Runner…</option>
                                  {stations.map((st) => (
                                    <option key={st.id} value={st.id}>{st.name}</option>
                                  ))}
                                </select>
                              )}
                            </div>
                          ) : (
                            <span className="text-xs text-muted">Estos roles no necesitan asignación</span>
                          )
                        ) : (
                          <AssignmentCell
                            roles={s.roles}
                            floorLabel={floorName(s.assignedFloorId)}
                            stationLabel={stationName(s.assignedStationId)}
                          />
                        )}
                      </td>
                      <td className="table-td">
                        <span className={`rounded-full px-2 py-0.5 text-xs font-medium tracking-[0.03em] ${STATUS_COLORS[s.status] ?? "bg-paper2 text-muted"}`}>
                          {STATUS_LABEL[s.status] ?? s.status}
                        </span>
                      </td>
                      {isAdmin && (
                        <td className="table-td">
                          <div className="flex flex-wrap gap-3">
                            {editingUserId === s.userId ? (
                              <>
                                <button onClick={() => handleSaveMember(s)} disabled={savingRow} className="text-xs font-medium text-emerald-700 underline disabled:opacity-50">
                                  {savingRow ? "Guardando…" : "Guardar"}
                                </button>
                                <button onClick={() => setEditingUserId(null)} disabled={savingRow} className="text-xs font-medium text-muted underline disabled:opacity-50">Cancelar</button>
                              </>
                            ) : (
                              <>
                                {s.status === "PENDING" && (
                                  <button onClick={() => handleResend(s)} className="text-xs font-medium text-brand-700 underline">Reenviar enlace</button>
                                )}
                                {s.status === "ACTIVE" && (
                                  <button onClick={() => handleResetPassword(s)} className="text-xs font-medium text-brand-700 underline">Resetear contraseña</button>
                                )}
                                <button onClick={() => startEditing(s)} className="text-xs font-medium text-ink underline">Editar rol y asignación</button>
                                <button onClick={() => handleRemove(s)} className="text-xs font-medium text-rose-600 underline">Quitar</button>
                              </>
                            )}
                          </div>
                        </td>
                      )}
                    </tr>
                    {rowError[s.userId] && (
                      <tr>
                        <td colSpan={colSpan} className="px-4 py-1 text-xs text-rose-600">{rowError[s.userId]}</td>
                      </tr>
                    )}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : (
        !inviting && (
          <div className="flex flex-col items-center gap-3 py-14 text-center">
            <div className="mb-1 flex w-28 items-center gap-2.5 text-brand-500">
              <span className="h-px flex-1 bg-current opacity-45" />
              <span className="text-[11px] leading-none">✦</span>
              <span className="h-px flex-1 bg-current opacity-45" />
            </div>
            <p className="text-sm text-muted">
              {isAdmin ? "Usa el botón \"+ Invitar\" para agregar colaboradores al equipo." : "No tienes permisos para gestionar el personal."}
            </p>
          </div>
        )
      )}
    </div>
  );
}

/**
 * Quién atiende qué, de un vistazo. Si el rol exige asignación y falta, se marca en ámbar: un PDA
 * sin sector o un Runner sin estación no puede trabajar y hay que verlo sin abrir la fila.
 */
function AssignmentCell({
  roles,
  floorLabel,
  stationLabel,
}: {
  roles: string[];
  floorLabel: string | null;
  stationLabel: string | null;
}) {
  const faltaSector = needsFloor(roles) && !floorLabel;
  const faltaEstacion = needsStation(roles) && !stationLabel;

  if (!floorLabel && !stationLabel && !faltaSector && !faltaEstacion) {
    return <span className="text-muted">—</span>;
  }

  return (
    <div className="flex flex-col gap-1">
      {floorLabel && (
        <span className="inline-flex w-fit items-center gap-1 rounded-full bg-oro/[0.10] px-2 py-0.5 text-xs font-medium tracking-[0.03em] text-brand-700">
          <MapPin size={13} weight="light" aria-hidden />
          {floorLabel}
        </span>
      )}
      {stationLabel && (
        <span className="inline-flex w-fit items-center gap-1 rounded-full bg-sky-500/10 px-2 py-0.5 text-xs font-medium tracking-[0.03em] text-sky-700">
          <Monitor size={13} weight="light" aria-hidden />
          {stationLabel}
        </span>
      )}
      {faltaSector && (
        <span className="inline-flex w-fit items-center gap-1 rounded-full bg-amber-500/10 px-2 py-0.5 text-xs font-medium tracking-[0.03em] text-amber-700">
          <Warning size={13} weight="light" aria-hidden />
          Sin sector
        </span>
      )}
      {faltaEstacion && (
        <span className="inline-flex w-fit items-center gap-1 rounded-full bg-amber-500/10 px-2 py-0.5 text-xs font-medium tracking-[0.03em] text-amber-700">
          <Warning size={13} weight="light" aria-hidden />
          Sin estación
        </span>
      )}
    </div>
  );
}
