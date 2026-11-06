// Common types shared across all apps

/**
 * Roles del staff dentro de un restaurante (StaffRole en el backend).
 *
 * A los heredados (`Admin`, `Cajero`, `Mesero`, `KDS`) se suman los tres de Mesto Beach
 * (SDD-001 v2.0 — PRD-018): `Host` reparte mesas, `PDA` comanda en **un sector** y `Runner`
 * reparte desde **una estación**. `Mesero` es el alias heredado de `PDA` mientras conviven
 * ambas nomenclaturas (OQ-09 de PRD-018), pero solo `PDA` exige sector: el backend mira
 * exactamente ese rol, no el alias.
 */
export type Role =
  | "Admin"
  | "Cajero"
  | "Mesero"
  | "KDS"
  | "Host"
  | "PDA"
  | "Runner";

export type OpsRole = "soporte" | "billing" | "admin";

export interface ProblemDetails {
  title: string;
  detail: string;
  status: number;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

// ─── Catalog types ────────────────────────────────────────────────────────────

export type ModifierType = "SINGLE" | "MULTI" | "REMOVE";

export interface ModifierOptionDto {
  id: string;
  name: string;
  priceDeltaCents: number;
  available: boolean;
  isDefault: boolean;
}

/** A reusable add-on group from the tenant library, as returned by the admin API. */
export interface ModifierGroupDto {
  id: string;
  name: string;
  type: ModifierType;
  required: boolean;
  minSelect: number;
  maxSelect?: number | null;
  options: ModifierOptionDto[];
  usedByItemCount: number;
  createdAt: string;
  updatedAt: string;
}

/** Per-option payload when creating/updating a modifier group. */
export interface ModifierOptionInput {
  id?: string;
  name: string;
  priceDeltaCents: number;
  available?: boolean;
  isDefault?: boolean;
}

export interface UpsertModifierGroupRequest {
  name: string;
  type: ModifierType;
  required: boolean;
  minSelect: number;
  maxSelect?: number | null;
  options: ModifierOptionInput[];
}

/** Modifier group resolved & embedded per item in the public catalog. */
export interface PublicModifierGroupDto {
  id: string;
  name: string;
  type: ModifierType;
  required: boolean;
  minSelect: number;
  maxSelect?: number | null;
  options: ModifierOptionDto[];
}

export interface PublicCatalogCategoryDto {
  id: string;
  name: string;
  displayOrder: number;
}

export interface PublicCatalogItemDto {
  id: string;
  categoryId: string;
  name: string;
  description?: string;
  priceCents: number;
  imageUrl?: string;
  /** All product images (gallery). imageUrl mirrors images[0] for compat. */
  images?: string[];
  available: boolean;
  stockEnabled: boolean;
  stockQty: number;
  displayOrder: number;
  modifierGroups: PublicModifierGroupDto[];
  allowNotes: boolean;
}

/** "night" (ADR-039): preset oscuro del modo NIGHT. */
export type MenuTheme = "light" | "cream" | "dark" | "night";

/** Modos en los que se ve una categoría o un ítem (ADR-041). Nunca vacío. */
export type VisibleIn = Array<"RESTAURANT" | "NIGHT">;

/** Tenant-controlled look & feel for the customer menu. */
export interface MenuBrandingDto {
  displayName: string;
  slogan: string;
  logoUrl?: string | null;
  heroUrl?: string | null;
  /** Accent color as #RRGGBB. */
  primaryColor: string;
  theme: MenuTheme;
  /** Optional custom menu background as #RRGGBB. When set, overrides the theme preset. */
  backgroundColor?: string | null;
  /** Curated font pairing key. */
  fontKey: string;
}

export interface PublicCatalogResponse {
  tenantId: string;
  categories: PublicCatalogCategoryDto[];
  items: PublicCatalogItemDto[];
  branding: MenuBrandingDto;
  /** Modo activo (ADR-041): la carta viene filtrada y el branding resuelto para este modo. */
  venueMode: VenueMode;
}

// ── Admin catalog DTOs (StaffAdmin-only management endpoints) ──

export interface AdminCategoryDto {
  id: string;
  name: string;
  displayOrder: number;
  itemCount: number;
  createdAt: string;
  /** ADR-041. Ausente en Mongo → ["RESTAURANT","NIGHT"]. */
  visibleIn: VisibleIn;
}

export interface AdminItemDto {
  id: string;
  categoryId: string;
  categoryName: string;
  name: string;
  description?: string;
  priceCents: number;
  images: string[];
  available: boolean;
  stockEnabled: boolean;
  stockQty: number;
  lowStockThreshold: number;
  displayOrder: number;
  modifierGroupIds: string[];
  allowNotes: boolean;
  /** Estación de KDS propia del plato; null = hereda la de su categoría. */
  stationId?: string | null;
  /** ADR-041. El filtro «Todo | Día | Noche» del admin se aplica en el cliente. */
  visibleIn: VisibleIn;
  deletedAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface AdminItemsPagination {
  page: number;
  pageSize: number;
  total: number;
}

export interface GetAdminItemsResponse {
  items: AdminItemDto[];
  pagination: AdminItemsPagination;
}

export interface UploadImageResponse {
  imageIndex: number;
  url: string;
  thumbnailUrl: string;
  totalImages: number;
}

export interface DeleteImageResponse {
  remainingImages: string[];
  totalImages: number;
}

// ─── Sessions types ───────────────────────────────────────────────────────────

export type SessionStatus = "OPEN" | "CLOSED" | "PAUSED";
export type ServiceMode = "STAFFED" | "SELF_SERVICE";
export type QrMode = "FIXED" | "DYNAMIC";

export interface OpenSessionDto {
  sessionId: string;
  tenantId: string;
  tableCode: string;
  tableId: string;
  status: SessionStatus;
  qrMode: QrMode;
  serviceMode: ServiceMode;
  pinRequired: boolean;
  pin?: string;
  waiterId?: string;
  openedBy?: string;
  createdAt: string;
  participantId: string;
}

export interface JoinSessionDto {
  participantId: string;
  sessionId: string;
  tenantId: string;
  tableCode: string;
  isHost: boolean;
  pinRequired: boolean;
  pinValidated: boolean;
  serviceMode: ServiceMode;
  orderedItems: unknown[];
}

export interface ParticipantDto {
  participantId: string;
  deviceId?: string;
  isHost: boolean;
  pinValidated: boolean;
  joinedAt: string;
  displayName?: string | null;
  hasAccount?: boolean;
}

export interface AddParticipantDto {
  participantId: string;
  sessionId: string;
  deviceId: string;
  customerAccountId?: string | null;
  isHost: boolean;
  pinValidated: boolean;
  joinedAt: string;
}

export interface OrdersSummaryDto {
  totalItems: number;
  totalCents: number;
  pendingItems: number;
  deliveredItems: number;
}

export interface SessionDetailDto {
  sessionId: string;
  tenantId: string;
  tableCode: string;
  tableId: string;
  status: SessionStatus;
  qrMode: QrMode;
  serviceMode: ServiceMode;
  pinRequired: boolean;
  waiterId?: string;
  waiterName?: string;
  createdAt: string;
  closedAt?: string;
  participants: ParticipantDto[];
  ordersSummary: OrdersSummaryDto;
}

export interface SessionByTableDto {
  sessionId: string;
  tenantId: string;
  tableCode: string;
  status: SessionStatus;
  serviceMode: ServiceMode;
  pinRequired: boolean;
  participantCount: number;
  createdAt: string;
}

export type TableShape = "SQUARE" | "ROUND";

export interface TableDto {
  id: string;
  tenantId: string;
  tableCode: string;
  zone?: string | null;
  active: boolean;
  hasActiveSession?: boolean;
  activeSessionId?: string | null;
  // Map layout (nullable: ad-hoc tables have no placement)
  floorId?: string | null;
  posX?: number | null;
  posY?: number | null;
  seats?: number | null;
  shape?: TableShape;
  /** Access mode set by the admin: "FIXED" (printed QR) or "DYNAMIC" (rotating QR). */
  qrMode?: "FIXED" | "DYNAMIC";
  /** Whether a Fixed-QR session needs a PIN. false = "sin seguridad" (open). N/A for Dynamic. */
  requiresPin?: boolean;
  /** PRD-019: clase de puesto. */
  kind?: TableKind;
  createdAt: string;
  updatedAt: string;
}

/** PRD-019: clase de puesto. RESERVADO va primero en las colas de noche. */
export type TableKind = "TABLE" | "HIGH_TABLE" | "RESERVADO";

export interface TablesListResponse {
  tables: TableDto[];
  total: number;
}

// ─── Floors & floor plan (map of tables by level) ───────────────────────────────

export interface WallSegment {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}

export interface FloorDto {
  id: string;
  tenantId: string;
  name: string;
  sortOrder: number;
  width: number;
  height: number;
  walls: WallSegment[];
  createdAt: string;
  updatedAt: string;
  /** PRD-019: modos en los que el piso existe (["RESTAURANT"] = cerrado de noche). */
  visibleIn: VenueMode[];
}

/**
 * PRD-019: una mesa tocada en el editor para `PATCH /tables/layout` (distribución de un modo).
 * Los campos que no se envían conservan el valor de la fila; `floorId`/`stationId` a null los quitan.
 */
export interface TableLayoutPlacement {
  tableId: string;
  floorId?: string | null;
  posX?: number;
  posY?: number;
  /** En servicio en ese modo. Default true. */
  active?: boolean;
  /** Barra que sirve la mesa (solo tiene efecto en NIGHT). */
  stationId?: string | null;
}

export interface UpdateTablesLayoutResponse {
  mode: VenueMode;
  applied: number;
  skipped: number;
}

export interface FloorPlanFloor {
  id: string;
  name: string;
  sortOrder: number;
  width: number;
  height: number;
  walls: WallSegment[];
  /** PRD-019: modos en los que el piso existe. */
  visibleIn: VenueMode[];
}

/** PRD-019: parámetros de `GET /floorplan`. */
export interface FloorPlanParams {
  /** Modo pedido; por defecto el activo. */
  mode?: VenueMode;
  /** Solo Admin: pisos ocultos y mesas apagadas (por modo o global). */
  includeHidden?: boolean;
  /** Host y Admin: mesas apagadas en el modo cuyo piso está visible. */
  includeInactive?: boolean;
}

export interface FloorPlanTable {
  id: string;
  tableCode: string;
  floorId?: string | null;
  posX?: number | null;
  posY?: number | null;
  seats?: number | null;
  shape: TableShape;
  active: boolean;
  hasActiveSession: boolean;
  activeSessionId?: string | null;
  /** Access mode set by the admin: "FIXED" or "DYNAMIC". */
  qrMode?: "FIXED" | "DYNAMIC";
  /** Whether a Fixed-QR session needs a PIN. false = "sin seguridad". */
  requiresPin?: boolean;
  /** Comensales registrados por la Host en la sesión activa (RF-1840). Null si libre o sin pax. */
  guestCount?: number | null;
  /** PRD-019: clase de puesto. */
  kind?: TableKind;
  /** PRD-019: interruptor global (tables.active). `active` es el del modo; en servicio = ambos. */
  activeGlobal?: boolean;
  /** PRD-019: barra de la fila del modo servido. */
  stationId?: string | null;
}

export interface FloorPlanDto {
  /** PRD-019: modo servido. */
  mode: VenueMode;
  /** PRD-019: modo del local ahora mismo. */
  activeMode: VenueMode;
  floors: FloorPlanFloor[];
  tables: FloorPlanTable[];
}

export interface SessionPinDto {
  sessionId: string;
  tableCode: string;
  pin: string;
}

export interface SessionJoinTokenDto {
  sessionId: string;
  tenantId: string;
  tableCode: string;
  tokenType: "DYNAMIC_QR" | "INVITE";
  token: string;
  joinUrl: string;
}

// ─── Customers CRM (Platform) ───────────────────────────────────────────────────

export type CustomerSegment = "nuevo" | "recurrente" | "vip" | "inactivo";

export interface CustomerCrmListItem {
  id: string;
  name: string;
  email: string;
  phone?: string | null;
  loyaltyPoints: number;
  orders: number;
  totalSpentCents: number;
  visits: number;
  lastActivity?: string | null;
  segment: CustomerSegment;
  tags: string[];
}

export interface CustomersListResponse {
  data: CustomerCrmListItem[];
  pagination: { page: number; limit: number; total: number; total_pages: number };
}

export interface CustomerFavoriteRestaurant {
  tenantId: string;
  name: string;
  visits: number;
}

export interface CustomerRecentOrder {
  orderId: string;
  tenantId: string;
  tenantName: string;
  createdAt: string;
  amountCents: number;
  items: number;
}

export interface CustomerCrmNote {
  text: string;
  createdBy: string;
  createdByName?: string | null;
  createdAt: string;
}

export interface CustomerCrmDetailDto {
  id: string;
  name: string;
  email: string;
  phone?: string | null;
  loyaltyPoints: number;
  createdAt: string;
  lastLogin: string;
  orders: number;
  totalSpentCents: number;
  avgTicketCents: number;
  visits: number;
  lastActivity?: string | null;
  segment: CustomerSegment;
  favorites: CustomerFavoriteRestaurant[];
  recentOrders: CustomerRecentOrder[];
  tags: string[];
  notes: CustomerCrmNote[];
}

export interface WaiterCallDto {
  tenantId: string;
  sessionId: string;
  tableCode: string;
  createdAt: string;
  /**
   * "WAITER" = llamada genérica; "PIN" = el comensal pide el PIN de la mesa para poder pedir;
   * "BILL" = pide la cuenta; "ETA" = pregunta "¿cuándo sale?" por su pedido.
   */
  reason?: "WAITER" | "PIN" | "BILL" | "ETA";
  /** PRD-019 (SDD-003): clase del puesto, resuelta al listar GET /sessions/calls. En NIGHT la lista sale con RESERVADO primero. */
  tableKind?: TableKind | null;
}

export interface ValidatePinDto {
  validated: boolean;
  participantId: string;
  sessionId: string;
}

export interface CloseSessionDto {
  sessionId: string;
  status: "CLOSED";
  closedAt: string;
  closedBy: string;
  tableCode: string;
  duration: number;
}

// ─── Orders types ─────────────────────────────────────────────────────────────

export type OrderStatus =
  | "PENDING"
  | "CONFIRMED"
  | "IN_PREPARATION"
  | "READY"
  | "DELIVERED"
  | "CANCELLED";

/** Estado del ciclo de vida por ítem (ADR-025). El plato es la unidad de estado, no la comanda. */
export type OrderItemStatus =
  | "HELD"
  /** En cola: marchado, esperando a que la cocina dé la orden de preparación. */
  | "QUEUED"
  | "IN_PREPARATION"
  | "READY"
  | "IN_DELIVERY"
  | "DELIVERED"
  | "RETURNED"
  | "CANCELLED";

/** Lo pidió "ahora" o "luego de mi instrucción" (ADR-026). */
export type ItemTiming = "NOW" | "LATER";

export interface OrderItemDto {
  id: string;
  catalogItemId: string;
  itemNameSnapshot: string;
  itemPriceSnapshotCents: number;
  quantity: number;
  notes?: string;
  loyaltyRedeemed: boolean;
  /** Human-readable modifier summary, e.g. "3/4 · sin Cebolla · + Michelada". */
  selectedModifiers?: string | null;
  // Ciclo por ítem (ADR-025). Opcionales: los clientes antiguos ignoran lo que no conocen.
  /** Estado de ESTE plato — pinta las dos colas del KDS y el gris de otras estaciones. */
  status?: OrderItemStatus | null;
  timing?: ItemTiming | null;
  /** Estación que prepara este plato. Cada ítem va a una sola estación (ADR-029). */
  stationId?: string | null;
  // Sellos por estado: el KDS pinta cuánto llevó el plato en cada paso. El tramo de un paso es
  // la diferencia con el siguiente sello; el del paso actual se calcula en vivo contra "ahora".
  /** Cuándo entró en la cola de su estación. */
  queuedAt?: string | null;
  /** Cuándo la cocina dio la orden de preparación. */
  preparationStartedAt?: string | null;
  /** Cuándo la estación lo dio por listo. */
  readyAt?: string | null;
  /** Cuándo se entregó en el puesto. */
  deliveredAt?: string | null;
}

export interface CreateOrderDto {
  id: string;
  sessionId: string;
  tenantId: string;
  participantId: string;
  status: OrderStatus;
  stationId?: string;
  cancellationReason?: string;
  createdAt: string;
  confirmedAt?: string;
  items: OrderItemDto[];
  /** Observación libre de la comanda entera. */
  observacion?: string | null;
}

/** Respuesta de POST /orders/items/{id}/march — el plato marchado. */
export interface MarchOrderItemDto {
  itemId: string;
  orderId: string;
  status: OrderItemStatus;
  stationId?: string | null;
  marchedAt: string;
}

/** Respuesta de POST /orders/items/{id}/start-preparation — el plato puesto en fuegos. */
export interface StartItemPreparationDto {
  itemId: string;
  orderId: string;
  status: OrderItemStatus;
  stationId?: string | null;
  preparationStartedAt: string;
}

/** Respuesta de POST /orders/items/{id}/ready — el plato dado por listo. */
export interface MarkOrderItemReadyDto {
  itemId: string;
  orderId: string;
  status: OrderItemStatus;
  stationId?: string | null;
  readyAt: string;
}

export interface OrderSummaryDto {
  id: string;
  sessionId: string;
  participantId: string;
  status: OrderStatus;
  stationId?: string;
  cancellationReason?: string;
  createdAt: string;
  confirmedAt?: string;
  itemCount: number;
  totalCents: number;
  items: OrderItemDto[];
  customerName?: string | null;
  /** Observación libre de la comanda entera. */
  observacion?: string | null;
}

export interface ListOrdersResponse {
  data: OrderSummaryDto[];
  total: number;
  truncated: boolean;
}

export type OrderDetailDto = CreateOrderDto;

/** KDS authoritative board row: order detail + resolved table code + readyAt. */
export interface ActiveKdsOrderDto extends CreateOrderDto {
  tableCode?: string | null;
  readyAt?: string | null;
  customerName?: string | null;
  /** Cursor field for KDS delta sync (P2). */
  updatedAt: string;
  /** PRD-019 / ADR-038: snapshot al crear — 1 si nació en NIGHT desde una mesa RESERVADO; `data` sale por priority DESC, createdAt ASC. */
  priority?: number;
  /** Modo del local al crear la comanda. Los históricos salen "RESTAURANT". */
  venueMode?: "RESTAURANT" | "NIGHT";
}

export interface ActiveOrdersResponse {
  /** Orders to upsert: all active (no cursor) or only those changed since the cursor. */
  data: ActiveKdsOrderDto[];
  total: number;
  /** Authoritative set of currently-active order ids — prune anything not here. */
  activeIds: string[];
  /** Server cursor (ISO-8601 UTC) to send as `since` on the next poll. */
  serverTime: string;
}

export interface UpdateOrderStatusDto {
  id: string;
  status: OrderStatus;
  confirmedAt?: string;
  updatedAt: string;
}

export interface CancelOrderDto {
  id: string;
  status: "CANCELLED";
  cancellationReason: string;
  cancelledAt: string;
  cancelledBy: string;
}

// ─── Entrega por viajes (ADR-027) — lo que ve y hace el runner ──────────────────

/** Un plato listo esperando runner, con su puesto y su posición en el mapa (RF-1830). */
export interface DeliveryQueueItemDto {
  itemId: string;
  orderId: string;
  sessionId: string;
  stationId?: string | null;
  name: string;
  quantity: number;
  notes?: string | null;
  selectedModifiers?: string | null;
  readyAt?: string | null;
  tableCode?: string | null;
  floorId?: string | null;
  posX?: number | null;
  posY?: number | null;
  customerName?: string | null;
  /** Observación libre de la comanda de la que sale el plato. */
  observacion?: string | null;
  /** PRD-019 / ADR-038: snapshot de la comanda; > 0 = «Reservado». La cola sale por priority DESC, readyAt ASC. */
  priority?: number;
}

export interface DeliveryQueueResponse {
  data: DeliveryQueueItemDto[];
  total: number;
  /** Cursor (ISO-8601 UTC): la cola también se sondea; el socket es solo un aviso. */
  serverTime: string;
}

/** Un plato dentro de una parada de la ruta. */
export interface DeliveryTripStopItemDto {
  itemId: string;
  orderId: string;
  name: string;
  quantity: number;
  notes?: string | null;
  selectedModifiers?: string | null;
  status: OrderItemStatus;
  /** Observación libre de la comanda de la que sale el plato. */
  observacion?: string | null;
  /** PRD-019: nombre del comensal; null = comanda del personal («Comanda de sala · {tableCode}»). */
  customerName?: string | null;
  /** PRD-019 / ADR-038: snapshot de la comanda; > 0 = parada «Reservado». Paradas por priority DESC, tableCode. */
  priority?: number;
}

/** Una parada de la ruta: el puesto, dónde está en el mapa y qué se le lleva. */
export interface DeliveryTripStopDto {
  tableCode?: string | null;
  floorId?: string | null;
  posX?: number | null;
  posY?: number | null;
  items: DeliveryTripStopItemDto[];
}

/** El manifiesto del viaje, ya agrupado por puesto — es lo que pinta el mapa del runner. */
export interface DeliveryTripDto {
  tripId: string;
  stationId?: string | null;
  status: string;
  createdAt: string;
  stops: DeliveryTripStopDto[];
}

export interface CompleteDeliveryTripDto {
  tripId: string;
  status: string;
  delivered: number;
  returned: number;
  finalizedOrderIds: string[];
}

export interface ReleaseDeliveryTripDto {
  tripId: string;
  status: string;
  releasedItems: number;
}

// ─── Payments types ───────────────────────────────────────────────────────────

export interface RegisterCashPaymentDto {
  paymentId: string;
  sessionId: string;
  amountCents: number;
  tipCents: number;
  totalCents: number;
  commissionCents: number;
  status: "COMPLETED";
  splitIndex?: number;
  splitTotal?: number;
  sessionClosed: boolean;
  createdAt: string;
}

export interface PaymentSummaryDto {
  id: string;
  amountCents: number;
  tipCents: number;
  totalCents: number;
  method: "CASH" | "DIGITAL";
  status: "COMPLETED" | "PENDING" | "FAILED";
  splitIndex?: number;
  splitTotal?: number;
  createdAt: string;
  completedAt?: string;
  payerLabel?: string | null;
}

export interface SessionPaymentStatusDto {
  sessionId: string;
  ordersTotalCents: number;
  payments: PaymentSummaryDto[];
  totalPaidCents: number;
  remainingCents: number;
  isFullyPaid: boolean;
}

export interface PaymentConfigDto {
  tenantId: string;
  digitalPaymentsEnabled: boolean;
  tipEnabled: boolean;
  /** Suggested tip percentages, e.g. [5, 10, 15]. */
  tipOptions: number[];
  gatewayType?: string;
}

export interface PaymentListItemDto {
  id: string;
  sessionId: string;
  amountCents: number;
  tipCents: number;
  totalCents: number;
  method: string;
  status: string;
  payerLabel?: string | null;
  createdAt: string;
}

/** Recent sales (payments) for the restaurant. */
export interface RecentPaymentsDto {
  count: number;
  totalCents: number;
  totalTipsCents: number;
  items: PaymentListItemDto[];
}

// ─── Shifts (cierre de turno) ───────────────────────────────────────────────────

export interface ShiftDto {
  id: string;
  tenantId: string;
  status: "OPEN" | "CLOSED";
  openingFloatCents: number;
  openedBy: string;
  openedAt: string;
  closedAt?: string;
  note?: string;
}

export interface ShiftMethodBreakdownDto {
  method: string;
  count: number;
  amountCents: number;
  tipCents: number;
}

export interface ShiftReportDto {
  shiftId: string;
  status: "OPEN" | "CLOSED";
  openedAt: string;
  closedAt?: string;
  openingFloatCents: number;
  totalSalesCents: number;
  totalTipsCents: number;
  totalCommissionCents: number;
  expectedCashCents: number;
  paymentCount: number;
  sessionCount: number;
  byMethod: ShiftMethodBreakdownDto[];
}

// ─── Closed sessions history ────────────────────────────────────────────────────

export interface ClosedSessionDto {
  sessionId: string;
  tableCode: string;
  createdAt: string;
  closedAt?: string;
  participantCount: number;
}

export interface ClosedSessionsPageDto {
  items: ClosedSessionDto[];
  total: number;
  page: number;
  pageSize: number;
}

// ─── Analytics types ──────────────────────────────────────────────────────────

export interface SalesSeriesPoint {
  period: string;
  revenueCents: number;
  sessionCount: number;
  orderCount: number;
}

export interface SalesSummaryDto {
  tenantId: string;
  from: string;
  to: string;
  granularity: string;
  totalRevenueCents: number;
  totalOrderCount: number;
  totalSessionCount: number;
  averageTicketCents: number;
  series: SalesSeriesPoint[];
}

export interface TopProductItem {
  catalogItemId: string;
  itemNameSnapshot: string;
  totalQuantity: number;
  totalRevenueCents: number;
  orderCount: number;
}

export interface TopProductsDto {
  tenantId: string;
  from: string;
  to: string;
  sort: string;
  products: TopProductItem[];
}

export interface SessionAveragesDto {
  tenantId: string;
  from: string;
  to: string;
  totalSessions: number;
  totalOrders: number;
  totalPaidSessions: number;
  averageTicketCents: number;
  averageSessionDurationMin: number;
  averageOrdersPerSession: number;
  averageItemsPerOrder: number;
}

export interface HourlyPoint {
  hour: number;
  orderCount: number;
  revenueCents: number;
}

export interface HourlyHeatmapDto {
  tenantId: string;
  from: string;
  to: string;
  timezone: string;
  hours: HourlyPoint[];
}

export interface StationPrepTime {
  stationId?: string;
  stationName: string;
  avgPrepTimeMin: number;
  p90PrepTimeMin: number;
  sampleCount: number;
}

export interface PrepTimeDto {
  tenantId: string;
  from: string;
  to: string;
  stations: StationPrepTime[];
}

// ─── Staff / Identity types ───────────────────────────────────────────────────

export interface StaffMemberDto {
  /** Global user id — actions (roles, reset, remove) reference this id. */
  userId: string;
  name: string;
  email: string;
  /** Roles in this tenant (a member may hold several). */
  roles: Role[];
  /** PENDING (account not activated yet) | ACTIVE | INACTIVE (membership). */
  status: string;
  tenantId: string;
  createdAt: string;
  /** Sector asignado. Obligatorio si los roles incluyen `PDA` (SDD-001 v2.0). */
  assignedFloorId?: string | null;
  /** Estación asignada. Obligatoria si los roles incluyen `Runner` (SDD-001 v2.0). */
  assignedStationId?: string | null;
}

/**
 * Respuesta de `PATCH /identity/staff/{userId}/assignment`. Devuelve los roles vigentes
 * junto a la asignación, porque quitar un rol descarta la asignación que ya no exige.
 */
export interface StaffAssignmentDto {
  userId: string;
  roles: Role[];
  assignedFloorId: string | null;
  assignedStationId: string | null;
}

/**
 * Cuerpo de `PATCH /identity/staff/{userId}/assignment`. Los dos campos viajan **siempre**:
 * es un reemplazo del estado de asignación, no un parche campo a campo, así que enviar
 * `null` es la forma de retirar el sector o la estación.
 */
export interface UpdateStaffAssignmentBody {
  assignedFloorId: string | null;
  assignedStationId: string | null;
}

export interface StaffInviteResponseDto extends StaffMemberDto {
  /** ACTIVATION_LINK | DIRECT_ACCESS | EXISTING_USER (email already had an account → access granted). */
  accessMode: "ACTIVATION_LINK" | "DIRECT_ACCESS" | "EXISTING_USER";
  /** Present only when accessMode = ACTIVATION_LINK. */
  activationUrl?: string;
  /** Present only when accessMode = DIRECT_ACCESS — shown once to the admin. */
  temporaryPassword?: string;
}

export interface StaffResendResponseDto {
  userId: string;
  activationUrl: string;
  expiresAt: string;
}

/** Admin-initiated staff password reset link. */
export interface StaffPasswordResetLinkDto {
  staffId: string;
  email: string;
  resetUrl: string;
  expiresAt: string;
}

/** Ops-initiated reset link for a tenant's owner Admin. */
export interface TenantAdminResetLinkDto {
  tenantId: string;
  staffId: string;
  email: string;
  resetUrl: string;
  expiresAt: string;
}

// ─── KDS Station types ────────────────────────────────────────────────────────

/** Perfil de flujo (ADR-037): `BAR` permite QUEUED → READY y monta el tablero de 2 columnas. */
export type KdsFlowProfile = "FULL_PASS" | "BAR";

export interface KdsStationDto {
  id: string;
  tenantId: string;
  name: string;
  categoryIds: string[];
  createdAt: string;
  /** Interruptor general (ADR-037). */
  isActive: boolean;
  flowProfile: KdsFlowProfile;
  /** Modos en los que se enciende; nunca vacía. */
  activeIn: VenueMode[];
  /** Calculado con el modo vigente al responder: `isActive && activeIn ∋ mode`. */
  isOn: boolean;
}

export interface UpdateKdsStationDto {
  id: string;
  tenantId: string;
  name: string;
  categoryIds: string[];
  updatedAt: string;
  isActive: boolean;
  flowProfile: KdsFlowProfile;
  activeIn: VenueMode[];
  isOn: boolean;
}

/**
 * Campos de ADR-037 en POST / PUT /api/v1/kds-stations, todos opcionales.
 * Errores: 400 `INVALID_FLOW_PROFILE`, `ACTIVE_IN_EMPTY`, `INVALID_MODE`.
 */
export interface KdsStationProfileInput {
  isActive?: boolean;
  flowProfile?: KdsFlowProfile;
  activeIn?: VenueMode[];
}

/**
 * 409 de POST /orders cuando un plato resuelve a una estación apagada en el modo vigente
 * (ADR-037). `stationId` viaja en la raíz del ProblemDetails.
 */
export interface StationClosedProblem {
  title: "STATION_CLOSED";
  status: 409;
  detail: string;
  stationId: string;
}

// ─── Platform types ───────────────────────────────────────────────────────────

export type TenantStatus = "PENDING" | "TRIAL" | "ACTIVE" | "SUSPENDED" | "CANCELLED";
export type TenantPlan = "none" | "commission" | "subscription" | "hybrid";
export type TrialStatus =
  | "en_trial"
  | "expirado"
  | "en_negociacion"
  | "convertido"
  | "perdido";

export interface PlatformPagination {
  page: number;
  limit: number;
  total: number;
  total_pages: number;
}

export interface TenantSummaryDto {
  id: string;
  name: string;
  slug: string;
  city: string | null;
  ownerEmail: string | null;
  status: TenantStatus;
  plan: TenantPlan;
  trialEndsAt: string | null;
  nit: string | null;
  nitVerified: boolean;
  createdAt: string;
}

export interface TenantsListResponse {
  data: TenantSummaryDto[];
  pagination: PlatformPagination;
}

export interface CreateTenantDto {
  id: string;
  name: string;
  slug: string;
  city: string | null;
  ownerEmail: string | null;
  status: TenantStatus;
  plan: TenantPlan;
  trialEndsAt: string | null;
  nit: string | null;
  nitVerified: boolean;
  createdAt: string;
  createdBy: string | null;
  ownerAdmin: OwnerAdminAccessDto;
}

export interface OwnerAdminAccessDto {
  staffId: string;
  tenantId: string;
  name: string;
  email: string;
  status: string;
  activationUrl: string | null;
}

export interface OpsUserRef {
  id: string;
  name: string;
}

export interface TrialNoteDto {
  text: string;
  createdBy: string;
  createdByName: string | null;
  createdAt: string;
}

export interface TrialPipelineDto {
  status: TrialStatus;
  assignedTo: OpsUserRef | null;
  notes: TrialNoteDto[];
  convertedAt: string | null;
  convertedBy: string | null;
  lostAt: string | null;
  lostBy: string | null;
}

export interface TenantBillingSummaryDto {
  totalPaidCents: number;
  lastPaymentDate: string | null;
  paymentCount: number;
}

export interface TenantActivityDto {
  totalOrders: number;
  lastOrderAt: string | null;
  activeSessionsNow: number;
}

export interface TenantDetailDto {
  id: string;
  name: string;
  slug: string;
  city: string | null;
  ownerEmail: string | null;
  status: TenantStatus;
  plan: TenantPlan;
  commissionRate: number | null;
  subscriptionAmountCents: number | null;
  internalNotes: string | null;
  nit: string | null;
  nitVerified: boolean;
  trialEndsAt: string | null;
  createdAt: string;
  createdBy: OpsUserRef | null;
  ownerAdmin: OwnerAdminAccessDto | null;
  trialPipeline: TrialPipelineDto | null;
  billingSummary: TenantBillingSummaryDto;
  activity: TenantActivityDto;
}

export interface BillingListItem {
  tenantId: string;
  tenantName: string;
  tenantStatus: string;
  plan: TenantPlan;
  subscriptionAmountCents: number | null;
  commissionRate: number | null;
  estimatedCommissionCents: number | null;
  amountOwedCents: number;
  amountPaidCents: number;
  balanceCents: number;
  paymentStatus: string;
}

export interface BillingSummaryBlock {
  totalTenantsBilled: number;
  totalAmountCents: number;
  totalPaidCents: number;
  totalPendingCents: number;
}

export interface BillingListResponse {
  period: string;
  summary: BillingSummaryBlock;
  data: BillingListItem[];
  pagination: PlatformPagination;
}

export interface BillingPeriodPayment {
  id: string;
  amountCents: number;
  paymentDate: string;
  reference: string;
  notes: string | null;
  recordedBy: string;
  createdAt: string;
}

export interface BillingPeriodDetail {
  period: string;
  amountOwedCents: number;
  amountPaidCents: number;
  balanceCents: number;
  paymentStatus: string;
  payments: BillingPeriodPayment[];
}

export interface BillingDetailResponse {
  tenantId: string;
  tenantName: string;
  plan: TenantPlan;
  subscriptionAmountCents: number | null;
  commissionRate: number | null;
  nit: string | null;
  nitVerified: boolean;
  billingHistory: BillingPeriodDetail[];
  totalPaidCents: number;
  outstandingCents: number;
}

export interface TrialSummaryDto {
  tenantId: string;
  tenantName: string;
  tenantCity: string | null;
  ownerEmail: string | null;
  trialStatus: TrialStatus;
  trialEndsAt: string | null;
  daysRemaining: number;
  assignedToId: string | null;
  assignedToName: string | null;
  notesCount: number;
  lastNoteAt: string | null;
  totalOrdersInTrial: number;
  tenantCreatedAt: string;
}

export interface TrialsListResponse {
  data: TrialSummaryDto[];
  pagination: PlatformPagination;
}

// ─── Planes / Facturación SaaS / Ledger (PRD-017) ──────────────────────────────

export type PlanCode = "basic" | "pro" | "enterprise";
export type InvoiceStatus = "issued" | "paid" | "partial" | "overdue" | "void";
export type InvoiceLineKind = "subscription" | "commission" | "discount";

export interface PlanEntitlements {
  planCode: PlanCode;
  name: string;
  billingMode: TenantPlan;
  priceCents: number | null;
  maxFloors: number | null;
  maxKdsStations: number | null;
  maxTables: number | null;
  maxStaff: number | null;
  loyalty: boolean;
  branding: boolean;
  analytics: "basic" | "advanced";
}

// ── Invoices (Platform Ops) ──
export interface InvoiceListItem {
  id: string;
  tenantId: string;
  number: string;
  period: string;
  subtotalCents: number;
  vatCents: number;
  totalCents: number;
  status: InvoiceStatus;
  issuedAt: string;
  dueAt: string;
}

export interface InvoicePage {
  items: InvoiceListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface InvoiceLine {
  concept: string;
  kind: InvoiceLineKind;
  amountCents: number;
}

export interface Invoice {
  id: string;
  tenantId: string;
  number: string;
  period: string;
  subtotalCents: number;
  vatCents: number;
  totalCents: number;
  status: InvoiceStatus;
  issuedAt: string;
  dueAt: string;
  lines: InvoiceLine[];
}

export interface InvoicePayment {
  id: string;
  amountCents: number;
  paymentDate: string;
  reference: string;
  notes: string | null;
  createdAt: string;
}

export interface InvoiceDetail {
  invoice: Invoice;
  paidCents: number;
  balanceCents: number;
  voidedReason: string | null;
  voidedAt: string | null;
  payments: InvoicePayment[];
}

export interface BillingRunSummary {
  period: string;
  considered: number;
  issued: number;
  skipped: number;
  failed: number;
}

export interface TrialExpirySummary { expired: number; }
export interface OverdueSummary { markedOverdue: number; }

// ── Ledger (Platform Ops) ──
export interface LedgerAccount {
  code: string;
  name: string;
  type: string;
  normalSide: string;
}

export interface StatementMovement {
  entryId: string;
  type: string;
  reference: string;
  period: string | null;
  description: string | null;
  debitCents: number;
  creditCents: number;
  balanceCents: number;
  postedAt: string;
}

export interface TenantStatement {
  tenantId: string;
  tenantName: string | null;
  openingBalanceCents: number;
  closingBalanceCents: number;
  movements: StatementMovement[];
}

export interface LedgerBalances {
  period: string;
  accountsReceivableCents: number;
  accountsPayableCents: number;
  incomeSubscriptionCents: number;
  incomeCommissionCents: number;
  vatPayableCents: number;
  discountsCents: number;
  balanced: boolean;
}

export interface JournalLineView {
  accountCode: string;
  debitCents: number;
  creditCents: number;
}

export interface JournalEntryView {
  id: string;
  tenantId: string | null;
  entryType: string;
  reference: string;
  period: string | null;
  description: string | null;
  amountCents: number;
  postedAt: string;
  lines: JournalLineView[];
}

export interface JournalPage {
  items: JournalEntryView[];
  total: number;
  page: number;
  pageSize: number;
}

// ── My Plan (admin / dueño, tenant-facing) ──
export interface MyPlanEntitlements {
  maxFloors: number | null;
  maxKdsStations: number | null;
  maxTables: number | null;
  maxStaff: number | null;
  loyalty: boolean;
  branding: boolean;
  analytics: "basic" | "advanced";
}

export interface MyInvoice {
  id: string;
  number: string;
  period: string;
  totalCents: number;
  status: InvoiceStatus;
  issuedAt: string;
  dueAt: string;
}

export interface MyPlanAccount {
  outstandingCents: number;
  invoices: MyInvoice[];
}

export interface MyPlan {
  planCode: PlanCode;
  planName: string;
  billingMode: TenantPlan;
  priceCents: number | null;
  entitlements: MyPlanEntitlements;
  account: MyPlanAccount;
}

// ─── Venue — modo del local (ADR-036, SDD-001 §7) ─────────────────────────────

/** Modo del local. En el wire siempre string en mayúsculas. */
export type VenueMode = "RESTAURANT" | "NIGHT";

/**
 * GET /api/v1/venue/{tenantId} (anónimo) y GET /api/v1/venue (personal/KDS).
 * `changedAt` se omite si el local nunca salió de RESTAURANT. `stations` solo viene en la
 * versión autenticada (GET /api/v1/venue): todas las estaciones, encendidas o no.
 */
export interface VenueStateDto {
  tenantId: string;
  mode: VenueMode;
  changedAt?: string;
  stations?: VenueStationDto[];
}

/** Estación vista desde /venue: `isOn` se calcula con el mismo `mode` de la respuesta (ADR-037). */
export interface VenueStationDto {
  id: string;
  name: string;
  flowProfile: KdsFlowProfile;
  isOn: boolean;
}

/** GET /api/v1/venue/preflight. `orphanItems` es solo aviso: no entra en `ready`. */
export interface VenuePreflightDto {
  ready: boolean;
  openSessions: number;
  activeItems: number;
  openTrips: number;
  orphanItems: number;
  tables: string[];
}

/** POST /api/v1/venue/mode → 200. `changed: false` si ya estaba en ese modo. */
export interface ChangeVenueModeResponse {
  mode: VenueMode;
  previous: VenueMode;
  changed: boolean;
  changedAt?: string;
}

/** Cuerpo del 409 VENUE_NOT_IDLE (ProblemDetails con los contadores en la raíz). */
export interface VenueNotIdleProblem {
  title: "VENUE_NOT_IDLE";
  status: 409;
  detail: string;
  openSessions: number;
  activeItems: number;
  openTrips: number;
  orphanItems: number;
  tables: string[];
}

export interface VenuePreflightSnapshot {
  openSessions: number;
  activeItems: number;
  openTrips: number;
  orphanItems: number;
  tables: string[];
}

export interface VenueHistoryItemDto {
  id: string;
  fromMode: VenueMode;
  toMode: VenueMode;
  changedBy: string;
  /** Se omite si la cuenta ya no existe. */
  changedByName?: string;
  changedAt: string;
  preflight: VenuePreflightSnapshot;
}

export interface VenueHistoryDto {
  data: VenueHistoryItemDto[];
  total: number;
}

/** Evento VENUE_MODE_CHANGED por OrdersHub, grupo tenant:{t}. */
export interface VenueModeChangedEvent {
  type: "VENUE_MODE_CHANGED";
  mode: VenueMode;
  previous: VenueMode;
  changedAt: string;
}

// ─── Branding Night (ADR-039 / ADR-041) ───────────────────────────────────────

/**
 * Subdocumento `branding.night` crudo: null = hereda del día, salvo `backgroundColor`
 * (null = sin fondo propio, manda el tema).
 */
export interface NightBrandingDto {
  enabled: boolean;
  displayName: string | null;
  slogan: string | null;
  logoUrl: string | null;
  heroUrl: string | null;
  primaryColor: string | null;
  theme: MenuTheme | null;
  backgroundColor: string | null;
  fontKey: string | null;
}

/** `GET/PUT /branding/night` y sus imágenes: lo guardado + la vista previa resuelta de noche. */
export interface NightBrandingResponse {
  night: NightBrandingDto;
  effective: MenuBrandingDto;
}

export interface UpdateNightBrandingRequest {
  enabled: boolean;
  displayName: string | null;
  slogan: string | null;
  primaryColor: string | null;
  theme: MenuTheme | null;
  backgroundColor: string | null;
  fontKey: string | null;
}

// ─── PIN de noche (PRD-019 — ADR-040) ─────────────────────────────────────────

/**
 * POST /api/v1/orders → 422 en NIGHT: comensal sin token de personal cuyo participante no tiene
 * nombre (SDD-004 §Enrutado por modo, fila d). Se evalúa antes del PIN y del catálogo.
 */
export interface NameRequiredProblem {
  title: "NAME_REQUIRED";
  status: 422;
  detail: string;
}

/** 503 de POST /sessions y POST /orders cuando no se puede leer el modo del local (Redis y Postgres caídos). */
export interface VenueModeUnavailableProblem {
  title: "VENUE_MODE_UNAVAILABLE";
  status: 503;
  detail: string;
}
