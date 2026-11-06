"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type {
  AdminCategoryDto,
  AdminItemDto,
  KdsStationDto,
  ModifierGroupDto,
  VisibleIn,
} from "@mestobeach/api-client";
import { catalogApi, kdsApi, formatEur, centsToEurInput, parseEurosToCents } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
import { useDialog } from "@/components/Dialog";
import { AddonsLibrary, ItemModifiersField } from "./_components/modifiers";
import { ArrowLeft, ArrowRight, DotsSixVertical } from "@phosphor-icons/react";

function errMsg(err: unknown, fallback: string): string {
  return (
    (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? fallback
  );
}

/** El orden de las categorías lo manda displayOrder: es el que ve el comensal. */
function ordenar(cats: AdminCategoryDto[]): AdminCategoryDto[] {
  return cats.slice().sort((a, b) => a.displayOrder - b.displayOrder);
}

/**
 * Mapa categoría → estación, con la misma regla que el backend: si varias estaciones
 * reclaman una categoría (mala configuración), gana la más antigua.
 */
function estacionPorCategoria(stations: KdsStationDto[]): Map<string, KdsStationDto> {
  const mapa = new Map<string, KdsStationDto>();
  const porAntiguedad = stations
    .slice()
    .sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  for (const st of porAntiguedad) {
    for (const catId of st.categoryIds) if (!mapa.has(catId)) mapa.set(catId, st);
  }
  return mapa;
}

// ── Form state shapes (numbers held as strings for editable inputs) ──

interface CategoryForm {
  id: string | null;
  name: string;
  displayOrder: string;
}

interface ItemForm {
  id: string | null;
  categoryId: string;
  name: string;
  description: string;
  priceEur: string;
  displayOrder: string;
  available: boolean;
  stockEnabled: boolean;
  stockQty: string;
  lowStockThreshold: string;
  images: string[];
  modifierGroupIds: string[];
  allowNotes: boolean;
  /** "" = hereda la estación de su categoría. */
  stationId: string;
}

const EMPTY_ITEM: ItemForm = {
  id: null,
  categoryId: "",
  name: "",
  description: "",
  priceEur: "",
  displayOrder: "0",
  available: true,
  stockEnabled: false,
  stockQty: "0",
  lowStockThreshold: "0",
  images: [],
  modifierGroupIds: [],
  allowNotes: false,
  stationId: "",
};

type CatalogView = "products" | "addons";
type VisFilter = "all" | "RESTAURANT" | "NIGHT";

// ADR-041: en qué modos se ve una categoría o un producto.
const VIS_MODES = [
  { mode: "RESTAURANT", label: "Día" },
  { mode: "NIGHT", label: "Noche" },
] as const;
const visibleIn = (v: VisibleIn | undefined): VisibleIn => (v && v.length > 0 ? v : ["RESTAURANT", "NIGHT"]);
const nextVisible = (cur: VisibleIn, mode: "RESTAURANT" | "NIGHT"): VisibleIn =>
  cur.includes(mode) ? cur.filter((m) => m !== mode) : [...cur, mode];

export default function CatalogPage() {
  const { staff } = useAuthStore();
  const dialog = useDialog();
  const tenantId = staff?.tenantId ?? process.env["NEXT_PUBLIC_TENANT_ID"] ?? "";
  const isAdmin = staff?.roles?.includes("Admin") ?? false;

  const [categories, setCategories] = useState<AdminCategoryDto[]>([]);
  const [items, setItems] = useState<AdminItemDto[]>([]);
  const [groups, setGroups] = useState<ModifierGroupDto[]>([]);
  const [stations, setStations] = useState<KdsStationDto[]>([]);
  // La categoría que se arrastra va en un ref, no solo en estado: el manejador de "drop"
  // tiene que leer el valor de ESTE arrastre, no el del último render.
  const dragCatRef = useRef<string | null>(null);
  const [dragCatId, setDragCatId] = useState<string | null>(null);
  const [reordering, setReordering] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [activeCat, setActiveCat] = useState<string | null>(null);
  const [view, setView] = useState<CatalogView>("products");
  const [visFilter, setVisFilter] = useState<VisFilter>("all");

  // Category modal
  const [catForm, setCatForm] = useState<CategoryForm | null>(null);
  const [catSubmitting, setCatSubmitting] = useState(false);
  const [catError, setCatError] = useState<string | null>(null);

  // Item modal
  const [itemForm, setItemForm] = useState<ItemForm | null>(null);
  const [itemSubmitting, setItemSubmitting] = useState(false);
  const [itemError, setItemError] = useState<string | null>(null);
  const [uploadingImage, setUploadingImage] = useState(false);

  const loadAll = useCallback(async () => {
    if (!tenantId) return;
    setLoading(true);
    setError(null);
    try {
      const [catsRes, itemsRes, groupsRes, stationsRes] = await Promise.all([
        catalogApi.getAdminCategories(tenantId),
        catalogApi.getAdminItems(tenantId, { pageSize: 100 }),
        catalogApi.getModifierGroups(tenantId),
        kdsApi.listStations(),
      ]);
      setCategories(ordenar(catsRes.data));
      setItems(itemsRes.data.items);
      setGroups(groupsRes.data);
      setStations(stationsRes.data.data);
      setActiveCat((prev) => prev ?? catsRes.data[0]?.id ?? null);
    } catch (err: unknown) {
      setError(errMsg(err, "No se pudo cargar el catálogo."));
    } finally {
      setLoading(false);
    }
  }, [tenantId]);

  useEffect(() => {
    void loadAll();
  }, [loadAll]);

  function flashSuccess(msg: string) {
    setSuccess(msg);
    setTimeout(() => setSuccess(null), 4000);
  }

  // ── Category actions ──────────────────────────────────────────────────────

  async function handleSaveCategory(e: React.FormEvent) {
    e.preventDefault();
    if (!catForm) return;
    setCatError(null);
    setCatSubmitting(true);
    try {
      const name = catForm.name.trim();
      // El orden ya no se teclea: se arrastra. Al crear, la categoría entra la última.
      const displayOrder = Number(catForm.displayOrder) || 0;
      if (catForm.id) {
        await catalogApi.updateCategory(tenantId, catForm.id, { name, displayOrder });
        flashSuccess(`Categoría "${name}" actualizada.`);
      } else {
        await catalogApi.createCategory(tenantId, { name, displayOrder });
        flashSuccess(`Categoría "${name}" creada.`);
      }
      setCatForm(null);
      await loadAll();
    } catch (err: unknown) {
      setCatError(errMsg(err, "No se pudo guardar la categoría."));
    } finally {
      setCatSubmitting(false);
    }
  }

  async function handleDeleteCategory(cat: AdminCategoryDto) {
    if (cat.itemCount > 0) {
      dialog.toast(
        `No se puede eliminar "${cat.name}": tiene ${cat.itemCount} producto(s). Muévelos o elimínalos primero.`,
        { tone: "error" }
      );
      return;
    }
    if (
      !(await dialog.confirm({
        message: `¿Eliminar la categoría "${cat.name}"?`,
        tone: "danger",
        confirmText: "Eliminar",
      }))
    )
      return;
    try {
      await catalogApi.deleteCategory(tenantId, cat.id);
      flashSuccess(`Categoría "${cat.name}" eliminada.`);
      if (activeCat === cat.id) setActiveCat(null);
      await loadAll();
    } catch (err: unknown) {
      setError(errMsg(err, "No se pudo eliminar la categoría."));
    }
  }

  /**
   * Persiste el orden de las categorías: guarda solo las que cambiaron de sitio.
   * Es el orden con el que el comensal ve la carta, así que se aplica ya en pantalla
   * (optimista) y se revierte recargando si el servidor rechaza alguna.
   */
  async function moverCategoria(catId: string, destino: number) {
    const actual = ordenar(categories);
    const desde = actual.findIndex((c) => c.id === catId);
    if (desde < 0 || destino < 0 || destino >= actual.length || desde === destino) return;

    const reordenadas = actual.slice();
    const [movida] = reordenadas.splice(desde, 1);
    reordenadas.splice(destino, 0, movida!);

    const conOrden = reordenadas.map((c, i) => ({ ...c, displayOrder: i }));
    const cambiadas = conOrden.filter(
      (c, i) => actual[actual.findIndex((a) => a.id === c.id)]!.displayOrder !== i
    );
    if (cambiadas.length === 0) return;

    setCategories(conOrden);
    setReordering(true);
    try {
      // En serie: son pocas y así un fallo no deja el resto a medias sin saber cuál.
      for (const c of cambiadas) {
        await catalogApi.updateCategory(tenantId, c.id, { displayOrder: c.displayOrder });
      }
      flashSuccess("Orden de las categorías actualizado.");
    } catch (err: unknown) {
      setError(errMsg(err, "No se pudo guardar el nuevo orden."));
      await loadAll();
    } finally {
      setReordering(false);
    }
  }

  // ── Item actions ──────────────────────────────────────────────────────────

  function openNewItem() {
    setItemError(null);
    setItemForm({
      ...EMPTY_ITEM,
      categoryId: activeCat ?? categories[0]?.id ?? "",
    });
  }

  function openEditItem(item: AdminItemDto) {
    setItemError(null);
    setItemForm({
      id: item.id,
      categoryId: item.categoryId,
      name: item.name,
      description: item.description ?? "",
      priceEur: centsToEurInput(item.priceCents),
      displayOrder: String(item.displayOrder),
      available: item.available,
      stockEnabled: item.stockEnabled,
      stockQty: String(item.stockQty),
      lowStockThreshold: String(item.lowStockThreshold),
      images: item.images,
      modifierGroupIds: item.modifierGroupIds ?? [],
      allowNotes: item.allowNotes ?? false,
      stationId: item.stationId ?? "",
    });
  }

  async function handleSaveItem(e: React.FormEvent) {
    e.preventDefault();
    if (!itemForm) return;
    setItemError(null);

    const name = itemForm.name.trim();
    const priceCents = parseEurosToCents(itemForm.priceEur);
    if (!itemForm.categoryId) return setItemError("Selecciona una categoría.");
    if (!name) return setItemError("El nombre es requerido.");
    if (priceCents === null || priceCents <= 0)
      return setItemError("Escribe un precio válido en euros, mayor que 0. Ej: 14,50");

    setItemSubmitting(true);
    try {
      const description = itemForm.description.trim();
      const stockEnabled = itemForm.stockEnabled;
      const stockQty = stockEnabled ? Number(itemForm.stockQty) || 0 : 0;
      const lowStockThreshold = stockEnabled ? Number(itemForm.lowStockThreshold) || 0 : 0;
      const displayOrder = Number(itemForm.displayOrder) || 0;

      if (itemForm.id) {
        await catalogApi.updateItem(tenantId, itemForm.id, {
          categoryId: itemForm.categoryId,
          name,
          description,
          priceCents,
          available: itemForm.available,
          stockEnabled,
          stockQty,
          lowStockThreshold,
          displayOrder,
          modifierGroupIds: itemForm.modifierGroupIds,
          allowNotes: itemForm.allowNotes,
          // En un PATCH, omitir stationId significa "no la toques"; para vaciarla y volver
          // a heredar la de la categoría hay que pedirlo explícitamente.
          ...(itemForm.stationId
            ? { stationId: itemForm.stationId }
            : { clearStation: true }),
        });
        flashSuccess(`Producto "${name}" actualizado.`);
      } else {
        await catalogApi.createItem(tenantId, {
          categoryId: itemForm.categoryId,
          name,
          priceCents,
          available: itemForm.available,
          stockEnabled,
          stockQty,
          lowStockThreshold,
          displayOrder,
          modifierGroupIds: itemForm.modifierGroupIds,
          allowNotes: itemForm.allowNotes,
          ...(description ? { description } : {}),
          ...(itemForm.stationId ? { stationId: itemForm.stationId } : {}),
        });
        flashSuccess(`Producto "${name}" creado.`);
      }
      setItemForm(null);
      await loadAll();
    } catch (err: unknown) {
      setItemError(errMsg(err, "No se pudo guardar el producto."));
    } finally {
      setItemSubmitting(false);
    }
  }

  async function handleDeleteItem(item: AdminItemDto) {
    if (
      !(await dialog.confirm({
        message: `¿Eliminar el producto "${item.name}"?`,
        tone: "danger",
        confirmText: "Eliminar",
      }))
    )
      return;
    try {
      await catalogApi.deleteItem(tenantId, item.id);
      flashSuccess(`Producto "${item.name}" eliminado.`);
      await loadAll();
    } catch (err: unknown) {
      setError(errMsg(err, "No se pudo eliminar el producto."));
    }
  }

  async function handleToggleAvailability(item: AdminItemDto) {
    // optimistic
    setItems((prev) =>
      prev.map((i) => (i.id === item.id ? { ...i, available: !i.available } : i))
    );
    try {
      await catalogApi.updateItem(tenantId, item.id, { available: !item.available });
    } catch (err: unknown) {
      setItems((prev) =>
        prev.map((i) => (i.id === item.id ? { ...i, available: item.available } : i))
      );
      setError(errMsg(err, "No se pudo actualizar la disponibilidad."));
    }
  }

  // Chips «Día / Noche» (ADR-041): mismo patrón optimista que la disponibilidad.
  async function handleToggleItemVisible(item: AdminItemDto, mode: "RESTAURANT" | "NIGHT") {
    const prevVis = visibleIn(item.visibleIn);
    const next = nextVisible(prevVis, mode);
    if (next.length === 0) {
      setError("Un producto debe verse al menos en un modo (Día o Noche).");
      return;
    }
    setItems((prev) => prev.map((i) => (i.id === item.id ? { ...i, visibleIn: next } : i)));
    try {
      await catalogApi.updateItem(tenantId, item.id, { visibleIn: next });
    } catch (err: unknown) {
      setItems((prev) => prev.map((i) => (i.id === item.id ? { ...i, visibleIn: prevVis } : i)));
      setError(errMsg(err, "No se pudo actualizar la visibilidad."));
    }
  }

  async function handleToggleCategoryVisible(cat: AdminCategoryDto, mode: "RESTAURANT" | "NIGHT") {
    const prevVis = visibleIn(cat.visibleIn);
    const next = nextVisible(prevVis, mode);
    if (next.length === 0) {
      setError("Una categoría debe verse al menos en un modo (Día o Noche).");
      return;
    }
    setCategories((prev) => prev.map((c) => (c.id === cat.id ? { ...c, visibleIn: next } : c)));
    try {
      await catalogApi.updateCategory(tenantId, cat.id, { visibleIn: next });
    } catch (err: unknown) {
      setCategories((prev) => prev.map((c) => (c.id === cat.id ? { ...c, visibleIn: prevVis } : c)));
      setError(errMsg(err, "No se pudo actualizar la visibilidad."));
    }
  }

  async function handleUploadImage(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = ""; // allow re-selecting the same file
    if (!file || !itemForm?.id) return;
    setItemError(null);
    setUploadingImage(true);
    try {
      const { data } = await catalogApi.uploadImage(tenantId, itemForm.id, file);
      setItemForm((prev) =>
        prev ? { ...prev, images: [...prev.images, data.url] } : prev
      );
      await loadAll();
    } catch (err: unknown) {
      setItemError(errMsg(err, "No se pudo subir la imagen (máx 3, ≤2MB, JPG/PNG/WebP)."));
    } finally {
      setUploadingImage(false);
    }
  }

  async function handleDeleteImage(index: number) {
    if (!itemForm?.id) return;
    try {
      const { data } = await catalogApi.deleteImage(tenantId, itemForm.id, index);
      setItemForm((prev) => (prev ? { ...prev, images: data.remainingImages } : prev));
      await loadAll();
    } catch (err: unknown) {
      setItemError(errMsg(err, "No se pudo eliminar la imagen."));
    }
  }

  // ── Render ──────────────────────────────────────────────────────────────

  if (!isAdmin) {
    return (
      <div className="p-6">
        <h1 className="mb-2 font-display text-2xl font-normal text-ink">Catálogo</h1>
        <div className="card p-6 text-sm text-muted">
          Solo un usuario con rol <strong>Admin</strong> puede gestionar el catálogo del
          restaurante.
        </div>
      </div>
    );
  }

  const passFilter = (v: VisibleIn | undefined) => visFilter === "all" || visibleIn(v).includes(visFilter);
  const visibleItems = (activeCat ? items.filter((i) => i.categoryId === activeCat) : items)
    .filter((i) => passFilter(i.visibleIn))
    .slice()
    .sort((a, b) => a.displayOrder - b.displayOrder);
  const disponibles = visibleItems.filter((i) => i.available);
  const deshabilitados = visibleItems.filter((i) => !i.available);

  const catsOrdenadas = ordenar(categories);
  const catsVisibles = catsOrdenadas.filter((c) => c.id === activeCat || passFilter(c.visibleIn));
  const stationDeCategoria = useMemo(() => estacionPorCategoria(stations), [stations]);
  const stationPorId = useMemo(
    () => new Map(stations.map((st) => [st.id, st])),
    [stations]
  );

  /** Qué estación atiende un plato y si la fijó él o la heredó de su categoría. */
  function estacionDe(item: AdminItemDto): { nombre: string | null; propia: boolean } {
    const propia = item.stationId ? stationPorId.get(item.stationId) : undefined;
    if (propia) return { nombre: propia.name, propia: true };
    const heredada = stationDeCategoria.get(item.categoryId);
    return { nombre: heredada?.name ?? null, propia: false };
  }

  return (
    <div className="p-6">
      <div className="mb-4 flex items-center justify-between">
        <h1 className="font-display text-2xl font-normal text-ink">Catálogo</h1>
        {view === "products" && (
          <div className="flex gap-2">
            <button
              onClick={() =>
                setCatForm({ id: null, name: "", displayOrder: String(categories.length) })
              }
              className="btn-secondary text-sm"
            >
              + Categoría
            </button>
            <button
              onClick={openNewItem}
              disabled={categories.length === 0}
              className="btn-primary text-sm"
              title={categories.length === 0 ? "Crea una categoría primero" : undefined}
            >
              + Producto
            </button>
          </div>
        )}
      </div>

      {/* View switcher */}
      <div className="mb-6 inline-flex rounded-lg bg-paper2 p-1 text-sm">
        <button
          onClick={() => setView("products")}
          className={`rounded-md px-4 py-1.5 font-semibold transition ${
            view === "products" ? "bg-paper text-ink shadow-warm-sm" : "text-muted"
          }`}
        >
          Productos
        </button>
        <button
          onClick={() => setView("addons")}
          className={`rounded-md px-4 py-1.5 font-semibold transition ${
            view === "addons" ? "bg-paper text-ink shadow-warm-sm" : "text-muted"
          }`}
        >
          Adiciones
          {groups.length > 0 && (
            <span className="ml-1.5 text-xs text-muted">({groups.length})</span>
          )}
        </button>
      </div>

      {success && (
        <div className="mb-4 rounded-lg border border-emerald-600/30 bg-emerald-500/10 px-4 py-2.5 text-sm text-emerald-700">
          {success}
        </div>
      )}
      {error && (
        <div className="mb-4 rounded-lg border border-rose-600/30 bg-rose-500/10 px-4 py-2.5 text-sm text-rose-700">
          {error}
        </div>
      )}

      {view === "addons" ? (
        <AddonsLibrary
          tenantId={tenantId}
          groups={groups}
          loading={loading}
          onChanged={loadAll}
          flash={flashSuccess}
          setError={setError}
        />
      ) : loading ? (
        <div className="flex justify-center py-16">
          <div className="h-8 w-8 animate-spin rounded-full border-4 border-hair/40 border-t-brand-500" />
        </div>
      ) : categories.length === 0 ? (
        <div className="card p-8 text-center text-sm text-muted">
          Aún no tienes categorías. Crea la primera para empezar a cargar tu menú.
        </div>
      ) : (
        <>
          {/* Filtro «Ver: Todo | Día | Noche» (RF-1918), en el cliente */}
          <div className="mb-3 flex flex-wrap items-center gap-2">
            <span className="text-xs font-semibold uppercase tracking-[0.08em] text-muted">Ver</span>
            <div role="group" aria-label="Filtrar por modo" className="inline-flex rounded-lg bg-paper2 p-1 text-sm">
              {([["all", "Todo"], ["RESTAURANT", "Día"], ["NIGHT", "Noche"]] as const).map(([v, label]) => (
                <button
                  key={v}
                  onClick={() => setVisFilter(v)}
                  aria-pressed={visFilter === v}
                  className={`min-h-[44px] rounded-md px-4 py-1.5 font-semibold transition ${
                    visFilter === v ? "bg-paper text-ink shadow-warm-sm" : "text-muted"
                  }`}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>

          {/* Category tabs — se arrastran para fijar el orden de la carta */}
          <div className="mb-2 flex flex-wrap items-center gap-2">
            <button
              onClick={() => setActiveCat(null)}
              className={`rounded-full px-4 py-1.5 font-display text-sm font-normal transition ${
                activeCat === null
                  ? "bg-brand-500 text-white shadow-warm-sm"
                  : "bg-paper2 text-muted hover:bg-paper"
              }`}
            >
              Todos
            </button>
            {catsVisibles.map((cat) => {
              const idx = catsOrdenadas.findIndex((c) => c.id === cat.id);
              return (
              <span
                key={cat.id}
                draggable={!reordering}
                onDragStart={() => {
                  dragCatRef.current = cat.id;
                  setDragCatId(cat.id);
                }}
                onDragEnd={() => {
                  dragCatRef.current = null;
                  setDragCatId(null);
                }}
                onDragOver={(e) => e.preventDefault()}
                onDrop={(e) => {
                  e.preventDefault();
                  const arrastrada = dragCatRef.current;
                  if (arrastrada && arrastrada !== cat.id) void moverCategoria(arrastrada, idx);
                  dragCatRef.current = null;
                  setDragCatId(null);
                }}
                className={`inline-flex items-center rounded-full transition ${
                  dragCatId === cat.id ? "opacity-40" : ""
                } ${
                  activeCat === cat.id
                    ? "bg-brand-500 text-white shadow-warm-sm"
                    : "bg-paper2 text-muted hover:bg-paper"
                }`}
              >
                <DotsSixVertical
                  size={16}
                  weight="light"
                  aria-hidden
                  className="ml-2 cursor-grab opacity-50"
                />
                <button
                  onClick={() => setActiveCat(cat.id)}
                  className="rounded-full py-1.5 pl-1.5 pr-4 font-display text-sm font-normal"
                >
                  {cat.name}
                  <span className="ml-1.5 opacity-70">({cat.itemCount})</span>
                  {visibleIn(cat.visibleIn).length === 1 && (
                    <span className="ml-1.5 text-xs opacity-70">
                      {visibleIn(cat.visibleIn)[0] === "NIGHT" ? "solo noche" : "solo día"}
                    </span>
                  )}
                </button>
              </span>
              );
            })}
          </div>
          <p className="mb-4 text-xs text-muted">
            Arrastra las categorías para cambiar el orden en que las ve el comensal.
          </p>

          {/* Category management bar */}
          {activeCat && (
            <div className="mb-4 flex items-center gap-3 text-xs">
              {(() => {
                const cat = categories.find((c) => c.id === activeCat);
                if (!cat) return null;
                return (
                  <>
                    <button
                      onClick={() =>
                        setCatForm({
                          id: cat.id,
                          name: cat.name,
                          displayOrder: String(cat.displayOrder),
                        })
                      }
                      className="font-medium text-brand-700 underline"
                    >
                      Editar categoría
                    </button>
                    <button
                      onClick={() => handleDeleteCategory(cat)}
                      className="font-medium text-rose-600 underline"
                    >
                      Eliminar categoría
                    </button>
                    <VisibleChips
                      value={visibleIn(cat.visibleIn)}
                      onToggle={(m) => void handleToggleCategoryVisible(cat, m)}
                      label={`categoría "${cat.name}"`}
                    />
                    {/* Alternativa a arrastrar, para teclado y pantallas pequeñas */}
                    <span className="ml-auto inline-flex items-center gap-1">
                      <button
                        onClick={() => {
                          const i = catsOrdenadas.findIndex((c) => c.id === cat.id);
                          void moverCategoria(cat.id, i - 1);
                        }}
                        disabled={reordering || catsOrdenadas[0]?.id === cat.id}
                        className="grid place-items-center rounded-md p-1 text-muted hover:bg-paper2 hover:text-ink disabled:opacity-30 [@media(pointer:coarse)]:h-11 [@media(pointer:coarse)]:w-11"
                        aria-label={`Mover "${cat.name}" antes`}
                        title="Mover antes"
                      >
                        <ArrowLeft size={16} weight="light" aria-hidden />
                      </button>
                      <button
                        onClick={() => {
                          const i = catsOrdenadas.findIndex((c) => c.id === cat.id);
                          void moverCategoria(cat.id, i + 1);
                        }}
                        disabled={
                          reordering ||
                          catsOrdenadas[catsOrdenadas.length - 1]?.id === cat.id
                        }
                        className="grid place-items-center rounded-md p-1 text-muted hover:bg-paper2 hover:text-ink disabled:opacity-30 [@media(pointer:coarse)]:h-11 [@media(pointer:coarse)]:w-11"
                        aria-label={`Mover "${cat.name}" después`}
                        title="Mover después"
                      >
                        <ArrowRight size={16} weight="light" aria-hidden />
                      </button>
                    </span>
                  </>
                );
              })()}
            </div>
          )}

          {/* Productos disponibles */}
          <ItemsTable
            items={disponibles}
            vacio="Sin productos disponibles en esta categoría."
            estacionDe={estacionDe}
            onToggle={handleToggleAvailability}
            onVisible={handleToggleItemVisible}
            onEdit={openEditItem}
            onDelete={handleDeleteItem}
          />

          {/* Los deshabilitados no desaparecen: bajan aquí, listos para reactivarse */}
          {deshabilitados.length > 0 && (
            <div className="mt-8">
              <h2 className="mb-2 font-display text-lg font-normal text-ink">
                Productos deshabilitados
                <span className="ml-2 text-sm text-muted">({deshabilitados.length})</span>
              </h2>
              <p className="mb-3 text-xs text-muted">
                No aparecen como pedibles en la carta. Vuelve a activar el interruptor para
                devolverlos.
              </p>
              <div className="opacity-75">
                <ItemsTable
                  items={deshabilitados}
                  vacio=""
                  estacionDe={estacionDe}
                  onToggle={handleToggleAvailability}
            onVisible={handleToggleItemVisible}
                  onEdit={openEditItem}
                  onDelete={handleDeleteItem}
                />
              </div>
            </div>
          )}
        </>
      )}

      {/* ── Category modal ── */}
      {catForm && (
        <Modal title={catForm.id ? "Editar categoría" : "Nueva categoría"} onClose={() => setCatForm(null)}>
          <form onSubmit={handleSaveCategory} className="space-y-4">
            {catError && (
              <div className="rounded-lg border border-rose-600/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-700">
                {catError}
              </div>
            )}
            <div>
              <label className="label">Nombre</label>
              <input
                className="input"
                value={catForm.name}
                onChange={(e) => setCatForm({ ...catForm, name: e.target.value })}
                maxLength={100}
                autoFocus
                required
              />
            </div>
            <p className="text-xs text-muted">
              El orden en el que la ve el comensal se cambia arrastrando la categoría en la
              lista, no aquí.
            </p>
            <div className="flex justify-end gap-2 pt-2">
              <button type="button" onClick={() => setCatForm(null)} className="btn-secondary">
                Cancelar
              </button>
              <button type="submit" disabled={catSubmitting} className="btn-primary">
                {catSubmitting ? "Guardando…" : "Guardar"}
              </button>
            </div>
          </form>
        </Modal>
      )}

      {/* ── Item modal ── */}
      {itemForm && (
        <Modal
          title={itemForm.id ? "Editar producto" : "Nuevo producto"}
          onClose={() => setItemForm(null)}
        >
          <form onSubmit={handleSaveItem} className="space-y-4">
            {itemError && (
              <div className="rounded-lg border border-rose-600/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-700">
                {itemError}
              </div>
            )}
            <div>
              <label className="label">Categoría</label>
              <select
                className="input"
                value={itemForm.categoryId}
                onChange={(e) => setItemForm({ ...itemForm, categoryId: e.target.value })}
                required
              >
                <option value="" disabled>
                  Selecciona…
                </option>
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="label">Nombre</label>
              <input
                className="input"
                value={itemForm.name}
                onChange={(e) => setItemForm({ ...itemForm, name: e.target.value })}
                maxLength={150}
                required
              />
            </div>
            <div>
              <label className="label">Descripción (opcional)</label>
              <textarea
                className="input"
                rows={2}
                maxLength={300}
                value={itemForm.description}
                onChange={(e) => setItemForm({ ...itemForm, description: e.target.value })}
              />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="label">Precio (€)</label>
                <input
                  type="number"
                  min={0.01}
                  step={0.01}
                  className="input"
                  value={itemForm.priceEur}
                  onChange={(e) => setItemForm({ ...itemForm, priceEur: e.target.value })}
                  required
                />
              </div>
              <div>
                <label className="label">Orden</label>
                <input
                  type="number"
                  min={0}
                  className="input"
                  value={itemForm.displayOrder}
                  onChange={(e) => setItemForm({ ...itemForm, displayOrder: e.target.value })}
                />
              </div>
            </div>

            <div>
              <label className="label">Estación de KDS</label>
              <select
                className="input"
                value={itemForm.stationId}
                onChange={(e) => setItemForm({ ...itemForm, stationId: e.target.value })}
              >
                <option value="">
                  {(() => {
                    const heredada = stationDeCategoria.get(itemForm.categoryId);
                    return heredada
                      ? `Hereda de la categoría (${heredada.name})`
                      : "Hereda de la categoría (sin estación)";
                  })()}
                </option>
                {stations.map((st) => (
                  <option key={st.id} value={st.id}>
                    {st.name}
                  </option>
                ))}
              </select>
              <p className="mt-1 text-xs text-muted">
                Normalmente basta con la estación de la categoría. Fíjala aquí solo para las
                excepciones: el plato que sale de otra partida.
              </p>
              {stations.length === 0 && (
                <p className="mt-1 text-xs text-amber-700">
                  Todavía no hay estaciones de KDS creadas.
                </p>
              )}
            </div>

            <label className="flex items-center gap-2 text-sm text-ink">
              <input
                type="checkbox"
                checked={itemForm.available}
                onChange={(e) => setItemForm({ ...itemForm, available: e.target.checked })}
              />
              Disponible para los clientes
            </label>

            <label className="flex items-center gap-2 text-sm text-ink">
              <input
                type="checkbox"
                checked={itemForm.stockEnabled}
                onChange={(e) => setItemForm({ ...itemForm, stockEnabled: e.target.checked })}
              />
              Controlar inventario (stock)
            </label>

            {itemForm.stockEnabled && (
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="label">Cantidad en stock</label>
                  <input
                    type="number"
                    min={0}
                    className="input"
                    value={itemForm.stockQty}
                    onChange={(e) => setItemForm({ ...itemForm, stockQty: e.target.value })}
                  />
                </div>
                <div>
                  <label className="label">Umbral de stock bajo</label>
                  <input
                    type="number"
                    min={0}
                    className="input"
                    value={itemForm.lowStockThreshold}
                    onChange={(e) =>
                      setItemForm({ ...itemForm, lowStockThreshold: e.target.value })
                    }
                  />
                </div>
              </div>
            )}

            {/* Modifiers + free comment */}
            <ItemModifiersField
              groups={groups}
              selectedIds={itemForm.modifierGroupIds}
              onToggle={(id) =>
                setItemForm((prev) =>
                  prev
                    ? {
                        ...prev,
                        modifierGroupIds: prev.modifierGroupIds.includes(id)
                          ? prev.modifierGroupIds.filter((g) => g !== id)
                          : [...prev.modifierGroupIds, id],
                      }
                    : prev
                )
              }
              allowNotes={itemForm.allowNotes}
              onAllowNotesChange={(v) =>
                setItemForm((prev) => (prev ? { ...prev, allowNotes: v } : prev))
              }
            />

            {/* Images — only after the item exists */}
            {itemForm.id && (
              <div>
                <label className="label">Imágenes ({itemForm.images.length}/3)</label>
                <div className="flex flex-wrap items-center gap-2">
                  {itemForm.images.map((url, idx) => (
                    <div key={url} className="relative">
                      {/* eslint-disable-next-line @next/next/no-img-element */}
                      <img src={url} alt="" className="h-16 w-16 rounded-lg object-cover" />
                      <button
                        type="button"
                        onClick={() => handleDeleteImage(idx)}
                        className="absolute -right-1.5 -top-1.5 flex h-5 w-5 items-center justify-center rounded-full bg-rose-500 text-xs text-white"
                        aria-label="Eliminar imagen"
                      >
                        ×
                      </button>
                    </div>
                  ))}
                  {itemForm.images.length < 3 && (
                    <label className="flex h-16 w-16 cursor-pointer items-center justify-center rounded-lg border-2 border-dashed border-hair/50 text-muted hover:border-brand-500 hover:text-brand-700">
                      {uploadingImage ? "…" : "+"}
                      <input
                        type="file"
                        accept="image/jpeg,image/png,image/webp"
                        className="hidden"
                        disabled={uploadingImage}
                        onChange={handleUploadImage}
                      />
                    </label>
                  )}
                </div>
              </div>
            )}
            {!itemForm.id && (
              <p className="text-xs text-muted">
                Podrás agregar imágenes después de crear el producto.
              </p>
            )}

            <div className="flex justify-end gap-2 pt-2">
              <button type="button" onClick={() => setItemForm(null)} className="btn-secondary">
                Cancelar
              </button>
              <button type="submit" disabled={itemSubmitting} className="btn-primary">
                {itemSubmitting ? "Guardando…" : "Guardar"}
              </button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  );
}

// ── Interruptor de disponibilidad ──
// Un botón role="switch": se lee como interruptor con teclado y lector de pantalla,
// y a simple vista se ve encendido o apagado sin tener que leer "Sí"/"No".
function Switch({
  checked,
  onChange,
  label,
}: {
  checked: boolean;
  onChange: () => void;
  label: string;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      title={label}
      onClick={onChange}
      className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition ${
        checked ? "bg-brand-500" : "bg-hair"
      }`}
    >
      <span
        className={`inline-block h-4 w-4 transform rounded-full bg-white shadow-warm-sm transition ${
          checked ? "translate-x-6" : "translate-x-1"
        }`}
      />
    </button>
  );
}

// ── Chips «Día / Noche» (ADR-041) ──
// Mismo patrón que el interruptor de disponibilidad: role="switch" por modo, optimista.
function VisibleChips({
  value,
  onToggle,
  label,
}: {
  value: VisibleIn;
  onToggle: (mode: "RESTAURANT" | "NIGHT") => void;
  label: string;
}) {
  return (
    <span className="inline-flex gap-1.5">
      {VIS_MODES.map(({ mode, label: text }) => {
        const on = value.includes(mode);
        return (
          <button
            key={mode}
            type="button"
            role="switch"
            aria-checked={on}
            aria-label={`${on ? "Ocultar" : "Mostrar"} ${label} en ${text.toLowerCase()}`}
            onClick={() => onToggle(mode)}
            className={`min-h-[44px] rounded-full border px-3.5 py-1 text-xs font-semibold transition ${
              on ? "border-oro bg-oro/[0.10] text-ink" : "border-hair/55 bg-paper text-muted hover:bg-paper2"
            }`}
          >
            {text}
          </button>
        );
      })}
    </span>
  );
}

// ── Tabla de productos (se usa dos veces: disponibles y deshabilitados) ──
function ItemsTable({
  items,
  vacio,
  estacionDe,
  onToggle,
  onVisible,
  onEdit,
  onDelete,
}: {
  items: AdminItemDto[];
  vacio: string;
  estacionDe: (item: AdminItemDto) => { nombre: string | null; propia: boolean };
  onToggle: (item: AdminItemDto) => void;
  onVisible: (item: AdminItemDto, mode: "RESTAURANT" | "NIGHT") => void;
  onEdit: (item: AdminItemDto) => void;
  onDelete: (item: AdminItemDto) => void;
}) {
  return (
    <div className="card overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full">
          <thead className="border-b border-hair/50 bg-paper2">
            <tr>
              <th className="table-th">Producto</th>
              <th className="table-th text-right">Precio</th>
              <th className="table-th">Stock</th>
              <th className="table-th">Estación</th>
              <th className="table-th">Disponible</th>
              <th className="table-th">Visible en</th>
              <th className="table-th text-right">Acciones</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-hair/40">
            {items.map((item) => {
              const estacion = estacionDe(item);
              return (
                <tr key={item.id} className="hover:bg-paper2">
                  <td className="table-td">
                    <div className="flex items-center gap-3">
                      {item.images[0] ? (
                        // eslint-disable-next-line @next/next/no-img-element
                        <img
                          src={item.images[0]}
                          alt={item.name}
                          className="h-10 w-10 rounded-lg object-cover"
                        />
                      ) : (
                        <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-oro/[0.08] text-oro/50">
                          ◆
                        </div>
                      )}
                      <div>
                        <p className="font-display text-base font-normal text-ink">{item.name}</p>
                        {item.description && (
                          <p className="line-clamp-1 text-xs text-muted">{item.description}</p>
                        )}
                      </div>
                    </div>
                  </td>
                  <td className="table-td text-right font-semibold">
                    {formatEur(item.priceCents)}
                  </td>
                  <td className="table-td">
                    {item.stockEnabled ? (
                      <span
                        className={
                          item.stockQty === 0
                            ? "font-semibold text-rose-600"
                            : item.stockQty <= item.lowStockThreshold
                              ? "font-semibold text-amber-600"
                              : "text-ink"
                        }
                      >
                        {item.stockQty}
                      </span>
                    ) : (
                      <span className="text-muted">N/A</span>
                    )}
                  </td>
                  <td className="table-td">
                    {estacion.nombre ? (
                      <span className="text-ink">
                        {estacion.nombre}
                        {!estacion.propia && (
                          <span className="ml-1.5 text-xs text-muted">por categoría</span>
                        )}
                      </span>
                    ) : (
                      <span className="text-xs text-amber-700">Sin estación</span>
                    )}
                  </td>
                  <td className="table-td">
                    <Switch
                      checked={item.available}
                      onChange={() => onToggle(item)}
                      label={
                        item.available
                          ? `Deshabilitar "${item.name}"`
                          : `Habilitar "${item.name}"`
                      }
                    />
                  </td>
                  <td className="table-td">
                    <VisibleChips
                      value={visibleIn(item.visibleIn)}
                      onToggle={(m) => onVisible(item, m)}
                      label={`"${item.name}"`}
                    />
                  </td>
                  <td className="table-td text-right">
                    <button
                      onClick={() => onEdit(item)}
                      className="text-xs font-medium text-brand-700 underline"
                    >
                      Editar
                    </button>
                    <button
                      onClick={() => onDelete(item)}
                      className="ml-3 text-xs font-medium text-rose-600 underline"
                    >
                      Eliminar
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {items.length === 0 && vacio && (
        <p className="py-8 text-center text-sm text-muted">{vacio}</p>
      )}
    </div>
  );
}

// ── Lightweight modal (packages/ui is empty, so define locally) ──
function Modal({
  title,
  onClose,
  children,
}: {
  title: string;
  onClose: () => void;
  children: React.ReactNode;
}) {
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-ink/40 p-4 pb-[max(1rem,env(safe-area-inset-bottom))]"
      onClick={onClose}
    >
      <div
        className="max-h-[90dvh] w-full max-w-md overflow-y-auto rounded-xl bg-paper p-6 shadow-warm-lg"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-4 flex items-center justify-between">
          <h2 className="font-display text-xl font-normal text-ink">{title}</h2>
          <button onClick={onClose} className="-mr-2 grid h-11 w-11 place-items-center text-muted hover:text-ink" aria-label="Cerrar">
            ✕
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}
