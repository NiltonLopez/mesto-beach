import axios, { type AxiosInstance } from "axios";

const API_URL = process.env["NEXT_PUBLIC_API_URL"] ?? "http://localhost:5011";

/**
 * Eventos de conectividad con la API.
 *
 * Cuando una petición no llega al servidor —red caída, dirección equivocada,
 * tiempo agotado— se emite `API_OFFLINE_EVENT` en `window`; cuando alguna vuelve
 * a responder, `API_ONLINE_EVENT`. Cada app los escucha y enseña su aviso, en vez
 * de quedarse en «cargando» para siempre sin decir nada.
 *
 * Se hace con eventos del navegador, y no con un callback, porque el cliente vive
 * fuera de React y así cualquier app puede engancharse sin que este paquete
 * dependa de React.
 */
export const API_OFFLINE_EVENT = "mestobeach:api-offline";
export const API_ONLINE_EVENT = "mestobeach:api-online";

function emitir(nombre: string) {
  if (typeof window !== "undefined") window.dispatchEvent(new CustomEvent(nombre));
}

/**
 * Texto que enseñar cuando una llamada falla.
 *
 * Distingue lo que el servidor rechazó de lo que nunca llegó a él: si no hubo
 * respuesta, el motivo es la conexión y decir «credenciales incorrectas» —o
 * cualquier otra suposición— manda a quien lo lee a buscar el problema donde no
 * está. Con respuesta, gana el `detail` que envía la API.
 */
export function describeApiError(err: unknown, siRechaza: string): string {
  const e = err as { response?: { data?: { detail?: string } } };
  if (!e?.response) {
    return "No se pudo conectar con el servidor. Comprueba la conexión e inténtalo de nuevo.";
  }
  return e.response.data?.detail ?? siRechaza;
}

export function createApiClient(getToken?: () => string | null): AxiosInstance {
  const client = axios.create({
    baseURL: API_URL,
    withCredentials: true,
    headers: { "Content-Type": "application/json" },
    // Sin límite, una dirección muerta deja la petición colgada hasta que se rinde
    // el sistema operativo —decenas de segundos— y durante todo ese rato la
    // pantalla se queda en «cargando» sin poder decir nada.
    timeout: 15_000,
  });

  if (getToken) {
    client.interceptors.request.use((config) => {
      const token = getToken();
      if (token) config.headers.Authorization = `Bearer ${token}`;
      return config;
    });
  }

  client.interceptors.response.use(
    (res) => {
      emitir(API_ONLINE_EVENT);
      return res;
    },
    (err) => {
      // Que no haya `response` significa que el servidor no contestó. Un 4xx o un
      // 5xx sí son respuesta: eso lo gestiona cada pantalla con su mensaje, y
      // avisar aquí de «sin conexión» sería mentir.
      const cancelada = axios.isCancel(err) || err?.code === "ERR_CANCELED";
      if (!cancelada && !err?.response) emitir(API_OFFLINE_EVENT);
      return Promise.reject(err);
    }
  );

  return client;
}

// ─── Identity types ────────────────────────────────────────────────────────────

export interface StaffDto {
  id: string;
  name: string;
  email: string;
  /** All roles the member holds in the active tenant (an admin may grant several). */
  roles: import("./types").Role[];
  tenantId: string;
  /** Display name of the active restaurant (tenant) the member signed into. */
  tenantName?: string;
  /** True when the member signed in with a temporary password and must change it before operating. */
  mustChangePassword: boolean;
  /** Restaurant lifecycle: "Active" | "Trial" | "Pending" | "Suspended". The admin is alerted when not live. */
  tenantStatus?: string;
  /** Sector del PDA — viaja también como claim `floorId` en el JWT (SDD-001 v2.0). */
  assignedFloorId?: string | null;
  /** Estación del Runner — viaja también como claim `stationId` en el JWT (SDD-001 v2.0). */
  assignedStationId?: string | null;
}

export interface CustomerDto {
  id: string;
  email: string;
  name: string;
  loyaltyPoints: number;
  isNewAccount?: boolean;
}

export interface StaffLoginResponse {
  token: string;
  tokenType: string;
  expiresIn: number;
  staff: StaffDto;
}

export interface StaffTenantOption {
  tenantId: string;
  tenantName: string;
  roles: string[];
}

export interface ResolveStaffTenantsResponse {
  tenants: StaffTenantOption[];
}

export interface CustomerLoginResponse {
  token: string;
  tokenType: string;
  expiresIn: number;
  customer: CustomerDto;
}

export interface ChallengeTokenResponse {
  challengeToken: string;
  setupRequired: boolean;
  expiresIn: number;
}

export interface PlatformAuthResponse {
  token: string;
  tokenType: string;
  expiresIn: number;
  user: {
    id: string;
    email: string;
    name: string;
    roles: string[];
  };
}

/**
 * Unified login response.
 * - TOTP configured → { challengeToken, setupRequired, expiresIn }
 * - No TOTP configured → { token, user, expiresIn }
 */
export type PlatformLoginResponse =
  | { challengeToken: string; setupRequired: boolean; expiresIn: number; token?: never; user?: never }
  | { token: string; user: PlatformAuthResponse["user"]; expiresIn: number; challengeToken?: never; setupRequired?: never };

// ─── Identity API ──────────────────────────────────────────────────────────────

export function createIdentityApi(client: AxiosInstance) {
  return {
    async loginStaff(email: string, password: string, tenantId: string) {
      return client.post<StaffLoginResponse>("/api/v1/identity/auth/login", {
        email,
        password,
        tenantId,
      });
    },

    /** Discovery step: tenants where (email, password) is valid. No token issued. */
    async resolveStaffTenants(email: string, password: string) {
      return client.post<ResolveStaffTenantsResponse>(
        "/api/v1/identity/auth/login/resolve",
        { email, password }
      );
    },

    async refreshStaffToken() {
      return client.post<{ token: string; tokenType: string; expiresIn: number }>(
        "/api/v1/identity/auth/refresh"
      );
    },

    async logout() {
      return client.post("/api/v1/identity/auth/logout");
    },

    async logoutAll() {
      return client.post("/api/v1/identity/auth/logout-all");
    },

    async getStaffProfile() {
      return client.get<StaffDto>("/api/v1/identity/auth/me");
    },

    /** Admin launches their own restaurant (Pending → live) so the rest of the staff can sign in. */
    async activateTenant() {
      return client.post<{ tenantId: string; name: string; status: string }>(
        "/api/v1/identity/tenant/activate"
      );
    },

    /** Changes the password of the authenticated staff member (used for forced first-login change). */
    async changePassword(data: {
      currentPassword: string;
      newPassword: string;
      confirmPassword: string;
    }) {
      return client.post("/api/v1/identity/auth/change-password", data);
    },

    async registerCustomer(data: {
      email: string;
      password: string;
      name: string;
      phone?: string;
      birthDate?: string;
    }) {
      return client.post<CustomerLoginResponse>("/api/v1/identity/customers/register", data);
    },

    async loginCustomer(email: string, password: string) {
      return client.post<CustomerLoginResponse>("/api/v1/identity/customers/login", {
        email,
        password,
      });
    },

    async googleAuth(idToken: string) {
      return client.post<CustomerLoginResponse>("/api/v1/identity/customers/google", { idToken });
    },

    async getCustomerProfile() {
      return client.get("/api/v1/identity/customers/me");
    },

    async loginPlatform(email: string, password: string) {
      return client.post<PlatformLoginResponse>("/api/v1/identity/platform/login", {
        email,
        password,
      });
    },

    async verifyTotp(totpCode: string) {
      return client.post<PlatformAuthResponse>("/api/v1/identity/platform/totp/verify", {
        totpCode,
      });
    },

    async setupTotp() {
      return client.post("/api/v1/identity/platform/totp/setup");
    },

    async recoverTotp(email: string, emergencyCode: string) {
      return client.post("/api/v1/identity/platform/totp/recover", { email, emergencyCode });
    },
  };
}

// ─── Catalog API ───────────────────────────────────────────────────────────────

export function createCatalogApi(client: AxiosInstance) {
  return {
    async getPublicCatalog(tenantId: string) {
      return client.get<import("./types").PublicCatalogResponse>(`/api/v1/catalog/${tenantId}`);
    },

    async invalidateCache(tenantId: string) {
      return client.post(`/api/v1/catalog/${tenantId}/cache-invalidate`);
    },

    // ── Categories (StaffAdmin) ──────────────────────────────────────────────
    async getAdminCategories(tenantId: string) {
      return client.get<import("./types").AdminCategoryDto[]>(
        `/api/v1/catalog/${tenantId}/categories`
      );
    },

    async createCategory(
      tenantId: string,
      /** visibleIn (ADR-041): omitir = ambos modos; [] → 400 VISIBLE_IN_EMPTY. */
      data: { name: string; displayOrder: number; visibleIn?: import("./types").VisibleIn }
    ) {
      return client.post<import("./types").AdminCategoryDto>(
        `/api/v1/catalog/${tenantId}/categories`,
        data
      );
    },

    async updateCategory(
      tenantId: string,
      categoryId: string,
      data: { name?: string; displayOrder?: number; visibleIn?: import("./types").VisibleIn }
    ) {
      return client.patch<import("./types").AdminCategoryDto>(
        `/api/v1/catalog/${tenantId}/categories/${categoryId}`,
        data
      );
    },

    async deleteCategory(tenantId: string, categoryId: string) {
      return client.delete(`/api/v1/catalog/${tenantId}/categories/${categoryId}`);
    },

    // ── Items (StaffAdmin) ───────────────────────────────────────────────────
    async getAdminItems(
      tenantId: string,
      params: {
        categoryId?: string;
        available?: boolean;
        includeDeleted?: boolean;
        page?: number;
        pageSize?: number;
      } = {}
    ) {
      return client.get<import("./types").GetAdminItemsResponse>(
        `/api/v1/catalog/${tenantId}/items`,
        { params }
      );
    },

    async createItem(
      tenantId: string,
      data: {
        categoryId: string;
        name: string;
        description?: string;
        priceCents: number;
        available?: boolean;
        stockEnabled?: boolean;
        stockQty?: number;
        lowStockThreshold?: number;
        displayOrder: number;
        modifierGroupIds?: string[];
        allowNotes?: boolean;
        /** Estación de KDS propia; omitir para que herede la de su categoría. */
        stationId?: string;
        /** ADR-041. Omitir = visible en ambos modos. */
        visibleIn?: import("./types").VisibleIn;
      }
    ) {
      return client.post<import("./types").AdminItemDto>(
        `/api/v1/catalog/${tenantId}/items`,
        data
      );
    },

    async updateItem(
      tenantId: string,
      itemId: string,
      data: Partial<{
        name: string;
        description: string;
        priceCents: number;
        categoryId: string;
        available: boolean;
        stockEnabled: boolean;
        stockQty: number;
        lowStockThreshold: number;
        displayOrder: number;
        modifierGroupIds: string[];
        allowNotes: boolean;
        /** Estación propia del plato. Omitir = no tocarla. */
        stationId: string;
        /** true la deja vacía: el plato vuelve a heredar la estación de su categoría. */
        clearStation: boolean;
        /** ADR-041. Se guarda normalizado y sin fusionarlo con el de la categoría. */
        visibleIn: import("./types").VisibleIn;
      }>
    ) {
      return client.patch<import("./types").AdminItemDto>(
        `/api/v1/catalog/${tenantId}/items/${itemId}`,
        data
      );
    },

    async deleteItem(tenantId: string, itemId: string) {
      return client.delete(`/api/v1/catalog/${tenantId}/items/${itemId}`);
    },

    // ── Modifier groups / add-on library (StaffAdmin) ────────────────────────
    async getModifierGroups(tenantId: string) {
      return client.get<import("./types").ModifierGroupDto[]>(
        `/api/v1/catalog/${tenantId}/modifier-groups`
      );
    },

    async createModifierGroup(
      tenantId: string,
      data: import("./types").UpsertModifierGroupRequest
    ) {
      return client.post<import("./types").ModifierGroupDto>(
        `/api/v1/catalog/${tenantId}/modifier-groups`,
        data
      );
    },

    async updateModifierGroup(
      tenantId: string,
      groupId: string,
      data: import("./types").UpsertModifierGroupRequest
    ) {
      return client.patch<import("./types").ModifierGroupDto>(
        `/api/v1/catalog/${tenantId}/modifier-groups/${groupId}`,
        data
      );
    },

    async deleteModifierGroup(tenantId: string, groupId: string) {
      return client.delete(`/api/v1/catalog/${tenantId}/modifier-groups/${groupId}`);
    },

    // ── Menu branding / appearance (StaffAdmin) ──────────────────────────────
    async getMenuBranding(tenantId: string) {
      return client.get<import("./types").MenuBrandingDto>(`/api/v1/catalog/${tenantId}/branding`);
    },

    async updateMenuBranding(
      tenantId: string,
      data: {
        displayName?: string;
        slogan?: string;
        primaryColor: string;
        theme: import("./types").MenuTheme;
        backgroundColor?: string | null;
        fontKey: string;
      }
    ) {
      return client.put<import("./types").MenuBrandingDto>(`/api/v1/catalog/${tenantId}/branding`, data);
    },

    /** kind: "logo" | "hero" */
    async uploadBrandingImage(tenantId: string, kind: "logo" | "hero", file: File) {
      const form = new FormData();
      form.append("file", file);
      return client.post<import("./types").MenuBrandingDto>(
        `/api/v1/catalog/${tenantId}/branding/${kind}/image`,
        form,
        { headers: { "Content-Type": "multipart/form-data" } }
      );
    },

    async deleteBrandingImage(tenantId: string, kind: "logo" | "hero") {
      return client.delete<import("./types").MenuBrandingDto>(
        `/api/v1/catalog/${tenantId}/branding/${kind}/image`
      );
    },

    // ── Branding Night (StaffAdmin, ADR-039) ─────────────────────────────────
    async getNightBranding(tenantId: string) {
      return client.get<import("./types").NightBrandingResponse>(
        `/api/v1/catalog/${tenantId}/branding/night`
      );
    },

    /** Reemplazo completo salvo imágenes; null = hereda del día (backgroundColor: usar el tema). */
    async updateNightBranding(tenantId: string, data: import("./types").UpdateNightBrandingRequest) {
      return client.put<import("./types").NightBrandingResponse>(
        `/api/v1/catalog/${tenantId}/branding/night`,
        data
      );
    },

    async uploadNightBrandingImage(tenantId: string, kind: "logo" | "hero", file: File) {
      const form = new FormData();
      form.append("file", file);
      return client.post<import("./types").NightBrandingResponse>(
        `/api/v1/catalog/${tenantId}/branding/night/${kind}/image`,
        form,
        { headers: { "Content-Type": "multipart/form-data" } }
      );
    },

    /** Quita la imagen de Night: vuelve a heredar la de día. */
    async deleteNightBrandingImage(tenantId: string, kind: "logo" | "hero") {
      return client.delete<import("./types").NightBrandingResponse>(
        `/api/v1/catalog/${tenantId}/branding/night/${kind}/image`
      );
    },

    // ── Images (StaffAdmin) ──────────────────────────────────────────────────
    async uploadImage(tenantId: string, itemId: string, file: File) {
      const form = new FormData();
      form.append("file", file);
      return client.post<import("./types").UploadImageResponse>(
        `/api/v1/catalog/${tenantId}/items/${itemId}/images`,
        form,
        { headers: { "Content-Type": "multipart/form-data" } }
      );
    },

    async deleteImage(tenantId: string, itemId: string, imageIndex: number) {
      return client.delete<import("./types").DeleteImageResponse>(
        `/api/v1/catalog/${tenantId}/items/${itemId}/images/${imageIndex}`
      );
    },
  };
}

// ─── Sessions API ──────────────────────────────────────────────────────────────

export function createSessionsApi(client: AxiosInstance) {
  return {
    async listTables(tenantId: string, params: { active?: boolean; zone?: string } = {}) {
      return client.get<import("./types").TablesListResponse>("/api/v1/tables", {
        params: { tenantId, ...params },
      });
    },

    async openSession(data: {
      tenantId: string;
      tableCode: string;
      serviceMode: "STAFFED" | "SELF_SERVICE";
      /** QR mode now comes from the table (admin-set). Ignored for existing tables; only used
       *  to seed ad-hoc cashdesk tables. Defaults to FIXED. */
      qrMode?: "FIXED" | "DYNAMIC";
      waiterJwt?: string;
      deviceId?: string;
      shiftId?: string;
      /** Comensales que registra la Host al sentar la mesa (RF-1840). */
      guestCount?: number;
    }) {
      return client.post<import("./types").OpenSessionDto>("/api/v1/sessions", { qrMode: "FIXED", ...data });
    },

    async joinSession(data: {
      tenantId: string;
      deviceId: string;
      joinToken: string;
      tokenType: "DYNAMIC_QR" | "INVITE";
      customerAccountJwt?: string;
    }) {
      return client.post<import("./types").JoinSessionDto>("/api/v1/sessions/join", data);
    },

    /** Fixed table QR: join the active session of a table (tenant + tableCode, no token). */
    async joinByTable(data: { tenantId: string; tableCode: string; deviceId: string }) {
      return client.post<import("./types").JoinSessionDto>("/api/v1/sessions/join-by-table", data);
    },

    /** Staff adds a participant to a session (e.g. taking an order for a customer without a phone). */
    async addParticipant(
      sessionId: string,
      data: { deviceId: string; customerAccountId?: string; isHost?: boolean }
    ) {
      return client.post<import("./types").AddParticipantDto>(
        `/api/v1/sessions/${sessionId}/participants`,
        data
      );
    },

    async getSession(sessionId: string, deviceId?: string) {
      return client.get<import("./types").SessionDetailDto>(`/api/v1/sessions/${sessionId}`, {
        params: deviceId ? { deviceId } : undefined,
      });
    },

    async getSessionByTable(tenantId: string, tableCode: string) {
      return client.get<import("./types").SessionByTableDto>("/api/v1/sessions/by-table", {
        params: { tenantId, tableCode },
      });
    },

    async closeSession(
      sessionId: string,
      closureReason: "PAYMENT_COMPLETED" | "MANUAL_CLOSE" | "SYSTEM_TIMEOUT"
    ) {
      return client.post<import("./types").CloseSessionDto>(
        `/api/v1/sessions/${sessionId}/close`,
        { closureReason }
      );
    },

    async getSessionPin(sessionId: string) {
      return client.get<import("./types").SessionPinDto>(`/api/v1/sessions/${sessionId}/pin`);
    },

    async regeneratePin(sessionId: string) {
      return client.post<{ sessionId: string; newPin: string; regeneratedAt: string }>(
        `/api/v1/sessions/${sessionId}/pin/regenerate`
      );
    },

    /** Customer validates the shared table PIN (given by the waiter) to unlock ordering. */
    async validatePin(
      sessionId: string,
      data: { tenantId: string; participantId: string; deviceId: string; pin: string }
    ) {
      return client.post<import("./types").ValidatePinDto>(
        `/api/v1/sessions/${sessionId}/pin/validate`,
        data
      );
    },

    /** Customer asks the waiter for the table PIN — notifies waiter/cashier live (PIN_REQUESTED). */
    async requestPin(sessionId: string) {
      return client.post<import("./types").WaiterCallDto>(`/api/v1/sessions/${sessionId}/request-pin`);
    },

    /** Identify the participant: set the display name and (if a customer JWT is sent) link the account. */
    async identifyParticipant(sessionId: string, participantId: string, data: { displayName: string }) {
      return client.post<{ participantId: string; displayName: string | null; linkedAccount: boolean }>(
        `/api/v1/sessions/${sessionId}/participants/${participantId}/identify`,
        data
      );
    },

    /** Generate a signed INVITE token to share the session — the invitee joins WITHOUT typing the PIN. */
    async createInviteLink(sessionId: string, tenantId: string) {
      return client.post<import("./types").SessionJoinTokenDto>(
        `/api/v1/sessions/${sessionId}/invite-link`,
        { tenantId }
      );
    },

    async getJoinLink(sessionId: string, type: "DYNAMIC_QR" | "INVITE") {
      return client.get<import("./types").SessionJoinTokenDto>(`/api/v1/sessions/${sessionId}/join-link`, {
        params: { type },
      });
    },

    async listWaiterCalls(tenantId: string) {
      return client.get<import("./types").WaiterCallDto[]>("/api/v1/sessions/calls", {
        params: { tenantId },
      });
    },

    async callWaiter(sessionId: string) {
      return client.post<import("./types").WaiterCallDto>(`/api/v1/sessions/${sessionId}/call-waiter`);
    },

    async acknowledgeWaiterCall(sessionId: string) {
      return client.post<import("./types").WaiterCallDto>(`/api/v1/sessions/${sessionId}/call-waiter/acknowledge`);
    },

    /** Customer "request the bill" — notifies the cashdesk/waiter in real time (BILL_REQUESTED). */
    async requestBill(sessionId: string) {
      return client.post<import("./types").WaiterCallDto>(`/api/v1/sessions/${sessionId}/request-bill`);
    },

    /**
     * El comensal pregunta "¿cuándo sale?" por su pedido. Avisa al PDA/Salón en tiempo real
     * (ETA_REQUESTED) y deja un llamado pendiente con motivo "ETA"; nunca llega a cocina.
     */
    async askEta(sessionId: string) {
      return client.post<import("./types").WaiterCallDto>(`/api/v1/sessions/${sessionId}/ask-eta`);
    },

    /** Closed-sessions history (cashier/admin). */
    async listClosedSessions(params: { from?: string; to?: string; tableCode?: string; page?: number; pageSize?: number } = {}) {
      return client.get<import("./types").ClosedSessionsPageDto>("/api/v1/sessions/closed", { params });
    },

    // ─── Tables admin (create/update/delete/layout) ─────────────────────────────
    async createTable(data: {
      tenantId: string;
      tableCode: string;
      zone?: string | null;
      active?: boolean;
      floorId?: string | null;
      posX?: number;
      posY?: number;
      seats?: number;
      shape?: "SQUARE" | "ROUND";
      qrMode?: "FIXED" | "DYNAMIC";
      requiresPin?: boolean;
      /** PRD-019: modo de la fila activa (la del otro modo nace inactiva). Por defecto, el activo. */
      mode?: import("./types").VenueMode;
      kind?: import("./types").TableKind;
    }) {
      return client.post<import("./types").TableDto>("/api/v1/tables", data);
    },

    /** PRD-019: la posición ya no se edita aquí (400): usa `updateTablesLayout` por modo. */
    async updateTable(
      tableId: string,
      patch: {
        tableCode?: string;
        zone?: string | null;
        active?: boolean;
        seats?: number;
        shape?: "SQUARE" | "ROUND";
        qrMode?: "FIXED" | "DYNAMIC";
        requiresPin?: boolean;
        kind?: import("./types").TableKind;
      }
    ) {
      return client.patch<import("./types").TableDto>(`/api/v1/tables/${tableId}`, patch);
    },

    async deleteTable(tableId: string) {
      return client.delete<boolean>(`/api/v1/tables/${tableId}`);
    },

    /** PRD-019: guarda la distribución de UN modo (solo las mesas tocadas). */
    async updateTablesLayout(
      mode: import("./types").VenueMode,
      tables: import("./types").TableLayoutPlacement[]
    ) {
      return client.patch<import("./types").UpdateTablesLayoutResponse>("/api/v1/tables/layout", {
        mode,
        tables,
      });
    },

    // ─── Floors + floor plan (map of tables by level) ───────────────────────────
    async listFloors() {
      return client.get<import("./types").FloorDto[]>("/api/v1/floors");
    },

    /**
     * Floors + tables + live status (Libre/Ocupada). Sin parámetros: el plano del modo activo
     * (lo que usan Caja, PDA, Runner, KDS). PRD-019: el editor pide `mode` + `includeHidden`.
     */
    async getFloorPlan(params: import("./types").FloorPlanParams = {}) {
      return client.get<import("./types").FloorPlanDto>("/api/v1/floorplan", { params });
    },

    async createFloor(name: string) {
      return client.post<import("./types").FloorDto>("/api/v1/floors", { name });
    },

    async renameFloor(floorId: string, name: string) {
      return client.patch<import("./types").FloorDto>(`/api/v1/floors/${floorId}`, { name });
    },

    /** PRD-019: nombre y/o modos en los que el piso existe (`visibleIn` no vacío). */
    async updateFloor(
      floorId: string,
      patch: { name?: string; visibleIn?: import("./types").VenueMode[] }
    ) {
      return client.patch<import("./types").FloorDto>(`/api/v1/floors/${floorId}`, patch);
    },

    async reorderFloors(orderedFloorIds: string[]) {
      return client.patch<import("./types").FloorDto[]>("/api/v1/floors/reorder", { orderedFloorIds });
    },

    async updateFloorLayout(
      floorId: string,
      layout: { width?: number; height?: number; walls?: import("./types").WallSegment[] }
    ) {
      return client.patch<import("./types").FloorDto>(`/api/v1/floors/${floorId}/layout`, layout);
    },

    async deleteFloor(floorId: string) {
      return client.delete<boolean>(`/api/v1/floors/${floorId}`);
    },
  };
}

// ─── Orders API ────────────────────────────────────────────────────────────────

export function createOrdersApi(client: AxiosInstance) {
  return {
    async createOrder(data: {
      sessionId: string;
      participantId: string;
      /** Observación libre para toda la comanda (alergias, "todo para llevar"…). */
      observacion?: string;
      items: {
        catalogItemId: string;
        quantity: number;
        notes?: string;
        loyaltyRedeemed?: boolean;
        selectedOptionIds?: string[];
        /** "ahora mismo" o "luego de mi instrucción" (ADR-026). Por defecto NOW en backend. */
        timing?: "NOW" | "LATER";
      }[];
    }) {
      return client.post<import("./types").CreateOrderDto>("/api/v1/orders", data);
    },

    async listOrders(sessionId: string, status?: string, stationId?: string) {
      return client.get<import("./types").ListOrdersResponse>("/api/v1/orders", {
        params: {
          sessionId,
          ...(status ? { status } : {}),
          ...(stationId ? { stationId } : {}),
        },
      });
    },

    async getOrder(orderId: string) {
      return client.get<import("./types").OrderDetailDto>(`/api/v1/orders/${orderId}`);
    },

    /**
     * KDS authoritative board: ALL active orders of the tenant (optionally one
     * station) in one query. Poll this as the source of truth — sockets are a hint.
     */
    async listActiveOrders(params: { stationId?: string; since?: string } = {}) {
      return client.get<import("./types").ActiveOrdersResponse>("/api/v1/orders/active", {
        params: {
          ...(params.stationId ? { stationId: params.stationId } : {}),
          ...(params.since ? { since: params.since } : {}),
        },
      });
    },

    async updateOrderStatus(orderId: string, status: string) {
      return client.patch<import("./types").UpdateOrderStatusDto>(
        `/api/v1/orders/${orderId}/status`,
        { status }
      );
    },

    async cancelOrder(orderId: string, reason: string) {
      return client.delete<import("./types").CancelOrderDto>(`/api/v1/orders/${orderId}`, {
        data: { reason },
      });
    },

    // ── Ciclo de vida por ítem (ADR-025) ──────────────────────────────────────
    // El plato es la unidad de estado. La estación da por listo un ítem; el PDA marcha
    // los retenidos. Cada uno devuelve el ítem actualizado.

    /** La estación da por listo un plato en preparación (o rehace uno devuelto). Rol KDS. */
    async markItemReady(itemId: string) {
      return client.post<import("./types").MarkOrderItemReadyDto>(
        `/api/v1/orders/items/${itemId}/ready`,
        {}
      );
    },

    /**
     * La cocina da la ORDEN DE PREPARACIÓN: el plato sale de la cola y entra en fuegos
     * (QUEUED → IN_PREPARATION). Solo la estación decide cuándo se empieza. Rol KDS.
     */
    async startItemPreparation(itemId: string) {
      return client.post<import("./types").StartItemPreparationDto>(
        `/api/v1/orders/items/${itemId}/start-preparation`,
        {}
      );
    },

    /**
     * Marcha un plato retenido: lo saca de "en espera" y lo mete en la COLA de su estación
     * (HELD → QUEUED). Lo pueden hacer la cocina y el PDA.
     */
    async marchItem(itemId: string) {
      return client.post<import("./types").MarchOrderItemDto>(
        `/api/v1/orders/items/${itemId}/march`,
        {}
      );
    },

    // ── Entrega por viajes (ADR-027) — el runner ──────────────────────────────
    // La estación sale del token del runner; Administración (sin estación) la pasa por query.

    /** Cola del pase: platos listos sin reclamar de la estación del runner. */
    async getDeliveryQueue(stationId?: string) {
      return client.get<import("./types").DeliveryQueueResponse>("/api/v1/delivery-queue", {
        params: stationId ? { stationId } : {},
      });
    },

    /** Reclama platos para la bandeja y abre el viaje (reclamo atómico, 409 si algo ya no está listo). */
    async createDeliveryTrip(itemIds: string[]) {
      return client.post<import("./types").DeliveryTripDto>("/api/v1/delivery-trips", { itemIds });
    },

    /** Manifiesto del viaje abierto del runner. 204 → no lleva bandeja (data undefined). */
    async getMyDeliveryTrip() {
      return client.get<import("./types").DeliveryTripDto | "">("/api/v1/delivery-trips/mine");
    },

    /** Cierra la ruta: todo entregado por defecto; los `returnedItemIds` vuelven a su estación. */
    async completeDeliveryTrip(tripId: string, returnedItemIds: string[] = []) {
      return client.post<import("./types").CompleteDeliveryTripDto>(
        `/api/v1/delivery-trips/${tripId}/complete`,
        { returnedItemIds }
      );
    },

    /** Libera un viaje abandonado: los platos vuelven a la cola del pase (READY). */
    async releaseDeliveryTrip(tripId: string) {
      return client.post<import("./types").ReleaseDeliveryTripDto>(
        `/api/v1/delivery-trips/${tripId}/release`,
        {}
      );
    },
  };
}

// ─── Payments API ──────────────────────────────────────────────────────────────

export function createPaymentsApi(client: AxiosInstance) {
  return {
    async registerCashPayment(data: {
      sessionId: string;
      amountCents: number;
      tipCents?: number;
      splitIndex?: number;
      splitTotal?: number;
      payerLabel?: string;
    }) {
      return client.post<import("./types").RegisterCashPaymentDto>("/api/v1/payments/cash", data);
    },

    async getSessionPaymentStatus(sessionId: string) {
      return client.get<import("./types").SessionPaymentStatusDto>(
        `/api/v1/payments/sessions/${sessionId}/status`
      );
    },

    /** Recent sales (payments) for the restaurant, newest first. Cajero/Admin. */
    async listRecentPayments(params: { from?: string; to?: string; limit?: number } = {}) {
      return client.get<import("./types").RecentPaymentsDto>("/api/v1/payments", { params });
    },

    /** Tip config of the tenant (tipEnabled, tipOptions) for the cashdesk tip buttons. */
    async getPaymentConfig() {
      return client.get<import("./types").PaymentConfigDto>("/api/v1/payments/config");
    },

    // ── Cashier shifts ──────────────────────────────────────────────────────
    async getOpenShift() {
      return client.get<import("./types").ShiftDto | null>("/api/v1/shifts/open");
    },
    async openShift(openingFloatCents = 0) {
      return client.post<import("./types").ShiftDto>("/api/v1/shifts", { openingFloatCents });
    },
    async closeShift(shiftId: string, note?: string) {
      return client.post<import("./types").ShiftReportDto>(`/api/v1/shifts/${shiftId}/close`, { note });
    },
    async getShiftReport(shiftId: string) {
      return client.get<import("./types").ShiftReportDto>(`/api/v1/shifts/${shiftId}/report`);
    },
  };
}

// ─── Analytics API ─────────────────────────────────────────────────────────────

export function createAnalyticsApi(client: AxiosInstance) {
  return {
    async getSalesSummary(
      from: string,
      to: string,
      granularity: "day" | "week" | "month" = "day"
    ) {
      return client.get<import("./types").SalesSummaryDto>("/api/v1/analytics/sales/summary", {
        params: { from, to, granularity },
      });
    },

    async getTopProducts(
      from: string,
      to: string,
      limit = 10,
      sort: "quantity" | "revenue" = "quantity"
    ) {
      return client.get<import("./types").TopProductsDto>("/api/v1/analytics/products/top", {
        params: { from, to, limit, sort },
      });
    },

    async getSessionAverages(from: string, to: string) {
      return client.get<import("./types").SessionAveragesDto>(
        "/api/v1/analytics/sessions/averages",
        { params: { from, to } }
      );
    },

    async getHourlyHeatmap(from: string, to: string) {
      return client.get<import("./types").HourlyHeatmapDto>(
        "/api/v1/analytics/heatmap/hourly",
        { params: { from, to } }
      );
    },

    async getPrepTime(from: string, to: string) {
      return client.get<import("./types").PrepTimeDto>("/api/v1/analytics/prep-time", {
        params: { from, to },
      });
    },
  };
}

// ─── Staff Management API ──────────────────────────────────────────────────────

export function createStaffManagementApi(client: AxiosInstance) {
  return {
    async listStaff() {
      return client.get<import("./types").StaffMemberDto[]>(
        "/api/v1/identity/staff"
      );
    },

    async inviteStaff(data: {
      name: string;
      email: string;
      roles: string[];
      /** ACTIVATION_LINK (default) or DIRECT_ACCESS (temporary password). */
      accessMode: "ACTIVATION_LINK" | "DIRECT_ACCESS";
    }) {
      return client.post<import("./types").StaffInviteResponseDto>(
        "/api/v1/identity/staff/invite",
        data
      );
    },

    /** Change a member's roles within the current tenant. */
    async updateStaffRoles(userId: string, roles: string[]) {
      return client.patch<import("./types").StaffMemberDto>(
        `/api/v1/identity/staff/${userId}/roles`,
        { roles }
      );
    },

    /**
     * Fija el sector del PDA y la estación del Runner (SDD-001 v2.0 — solo Admin).
     *
     * Los dos campos se envían siempre: el endpoint reemplaza el estado de asignación, así que
     * `null` es la forma de retirar el sector o la estación. Llámalo **después** de guardar los
     * roles: el backend decide qué es obligatorio mirando los roles ya persistidos.
     *
     * Errores propios: `ASSIGNMENT_REQUIRED` (422, el rol exige asignación y viene vacía),
     * `FLOOR_NOT_FOUND` / `STATION_NOT_FOUND` (404) y `RUNNER_HAS_OPEN_TRIP` (409, el runner
     * tiene una ruta sin cerrar y no puede cambiar de estación).
     */
    async updateStaffAssignment(
      userId: string,
      assignment: import("./types").UpdateStaffAssignmentBody
    ) {
      return client.patch<import("./types").StaffAssignmentDto>(
        `/api/v1/identity/staff/${userId}/assignment`,
        assignment
      );
    },

    /** Remove a member's access to the current tenant (keeps their global account). */
    async removeStaff(userId: string) {
      return client.delete(`/api/v1/identity/staff/${userId}`);
    },

    async activateStaff(data: {
      token: string;
      password: string;
      confirmPassword: string;
    }) {
      return client.post("/api/v1/identity/staff/activate", data);
    },

    /** Admin generates a password-reset link for an active staff member who lost their password. */
    async resetStaffPassword(userId: string) {
      return client.post<import("./types").StaffPasswordResetLinkDto>(
        `/api/v1/identity/staff/${userId}/reset-password`
      );
    },

    async resendInvitation(userId: string) {
      return client.post<import("./types").StaffResendResponseDto>(
        `/api/v1/identity/staff/${userId}/resend-invitation`
      );
    },

    async requestPasswordReset(email: string, tenantId: string) {
      return client.post("/api/v1/identity/staff/password-reset/request", {
        email,
        tenantId,
      });
    },

    async confirmPasswordReset(data: {
      token: string;
      newPassword: string;
      confirmPassword: string;
    }) {
      return client.post("/api/v1/identity/staff/password-reset/confirm", data);
    },
  };
}

// ─── KDS Stations API ──────────────────────────────────────────────────────────

export function createKdsApi(client: AxiosInstance) {
  return {
    async listStations() {
      return client.get<{ data: import("./types").KdsStationDto[]; total: number }>(
        "/api/v1/kds-stations"
      );
    },

    async createStation(
      data: { name: string; categoryIds?: string[] } & import("./types").KdsStationProfileInput
    ) {
      return client.post<import("./types").KdsStationDto>("/api/v1/kds-stations", data);
    },

    async updateStation(
      stationId: string,
      data: { name?: string; categoryIds?: string[] } & import("./types").KdsStationProfileInput
    ) {
      return client.put<import("./types").UpdateKdsStationDto>(
        `/api/v1/kds-stations/${stationId}`,
        data
      );
    },

    async deleteStation(stationId: string) {
      return client.delete(`/api/v1/kds-stations/${stationId}`);
    },
  };
}

/**
 * Modo del local (ADR-036, SDD-001 §7). Base path /api/v1/venue.
 * `getPublic` es anónimo (lo sondea el menú); el resto exige token.
 */
export function createVenueApi(client: AxiosInstance) {
  return {
    async getPublic(tenantId: string) {
      return client.get<import("./types").VenueStateDto>(`/api/v1/venue/${tenantId}`);
    },

    async get() {
      return client.get<import("./types").VenueStateDto>("/api/v1/venue");
    },

    async preflight() {
      return client.get<import("./types").VenuePreflightDto>("/api/v1/venue/preflight");
    },

    async changeMode(mode: import("./types").VenueMode) {
      return client.post<import("./types").ChangeVenueModeResponse>("/api/v1/venue/mode", { mode });
    },

    async history(limit?: number) {
      return client.get<import("./types").VenueHistoryDto>("/api/v1/venue/history", {
        ...(limit !== undefined ? { params: { limit } } : {}),
      });
    },
  };
}

export * from "./types";
export * from "./money";
