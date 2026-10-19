<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { openNode, whereFrom } from '../../lib/links.svelte';
  import { onChange } from '../../lib/events';
  import { WORLD, cluster, formatCoord, project, ringsToPath, topoRings, unproject } from '../../lib/geo';
  import Icon from '../Icon.svelte';

  /** Map (Release 1.2): notes with a location, on a world map that ships with Quantos (nothing loads from the web). */
  interface Place { id: string; title: string; type: string; workspace: string; lat: number; lng: number; name: string | null; tags: string[] }
  type Pinned = Place & { x: number; y: number };

  let places = $state<Place[]>([]);
  let countries = $state<string[]>([]);
  let everywhere = $state(false);
  let filter = $state('');
  let view = $state({ x: 0, y: 0, k: 1 });
  let stage = $state<HTMLDivElement | null>(null);
  let size = $state({ w: 800, h: 500 });
  let chosen = $state<string | null>(null);
  let placing = $state<{ id: string; title: string } | null>(null);
  let drag: { x: number; y: number; vx: number; vy: number; moved: boolean } | null = null;

  async function load() {
    try { places = await api.get<Place[]>(`map/places${everywhere ? '' : `?workspace=${encodeURIComponent(app.workspace)}`}`); loaded = true; } catch (e) { app.error(e); }
  }
  $effect(() => { void everywhere; void app.workspace; load(); });

  onMount(() => {
    // The world map is bundled (Natural Earth, public domain) and only loaded when the map opens.
    import('world-atlas/countries-50m.json').then((m) => {
      countries = topoRings((m as unknown as { default: Parameters<typeof topoRings>[0] }).default, 'countries').map((polygon) => ringsToPath(polygon));
    }).catch(() => (countries = []));
    const ro = new ResizeObserver(() => { if (stage) size = { w: stage.clientWidth, h: stage.clientHeight }; });
    if (stage) ro.observe(stage);
    let timer: ReturnType<typeof setTimeout>;
    const off = onChange((e) => { if (['Updated', 'Created', 'Trashed', 'Restored'].includes(e.kind)) { clearTimeout(timer); timer = setTimeout(load, 600); } });
    return () => { ro.disconnect(); off(); };
  });

  const shown = $derived(places.filter((p) => !filter.trim() || (p.title + ' ' + (p.name ?? '') + ' ' + p.tags.join(' ')).toLowerCase().includes(filter.trim().toLowerCase())));
  const pins = $derived(shown.map((p) => ({ ...p, ...project(p.lat, p.lng) })) as Pinned[]);
  const groups = $derived(cluster(pins, view.k));
  const picked = $derived(places.find((p) => p.id === chosen) ?? null);

  // Frame the pins (or the world) until the person pans or zooms themselves.
  let touched = false;
  let loaded = $state(false);
  $effect(() => { void pins; if (loaded && size.w > 100 && !touched) fit(); });

  function fit(list: Pinned[] = pins) {
    if (!list.length) { const k = Math.max(size.w / WORLD, size.h / (WORLD * 0.7)); view = { k, x: size.w / 2 - (WORLD / 2) * k, y: size.h / 2 - (WORLD * 0.42) * k }; return; }
    const xs = list.map((p) => p.x), ys = list.map((p) => p.y);
    const w = Math.max(0.02, Math.max(...xs) - Math.min(...xs)), h = Math.max(0.02, Math.max(...ys) - Math.min(...ys));
    const k = Math.min(list.length > 1 ? 2000 : 60, Math.max(size.w / WORLD, Math.min((size.w - 120) / w, (size.h - 120) / h)));
    const cx = (Math.max(...xs) + Math.min(...xs)) / 2, cy = (Math.max(...ys) + Math.min(...ys)) / 2;
    view = { k, x: size.w / 2 - cx * k, y: size.h / 2 - cy * k };
  }
  function zoomAt(f: number, px = size.w / 2, py = size.h / 2) {
    touched = true;
    const k = Math.min(2000, Math.max(size.w / WORLD / 1.5, view.k * f));
    view = { k, x: px - ((px - view.x) / view.k) * k, y: py - ((py - view.y) / view.k) * k };
  }
  function wheel(e: WheelEvent) { e.preventDefault(); const r = stage!.getBoundingClientRect(); zoomAt(Math.exp(-e.deltaY * 0.0015), e.clientX - r.left, e.clientY - r.top); }
  function down(e: PointerEvent) {
    // Pins, clusters and the card are buttons: capturing the pointer here would take their clicks.
    if ((e.target as Element).closest('button, .card')) return;
    stage!.setPointerCapture(e.pointerId);
    drag = { x: e.clientX, y: e.clientY, vx: view.x, vy: view.y, moved: false };
  }
  function move(e: PointerEvent) {
    if (!drag) return;
    if (Math.abs(e.clientX - drag.x) + Math.abs(e.clientY - drag.y) > 3) { drag.moved = true; touched = true; }
    view = { ...view, x: drag.vx + e.clientX - drag.x, y: drag.vy + e.clientY - drag.y };
  }
  async function up(e: PointerEvent) {
    const d = drag; drag = null;
    if (!d || d.moved) return;
    if (placing) {
      const r = stage!.getBoundingClientRect();
      const { lat, lng } = unproject((e.clientX - r.left - view.x) / view.k, (e.clientY - r.top - view.y) / view.k);
      const p = placing; placing = null;
      try {
        await api.patch(`nodes/${p.id}`, { set: { location: formatCoord(lat, lng) } });
        app.toast(`Placed “${p.title}” at ${formatCoord(lat, lng)}.`, 'ok');
        chosen = p.id;
        await load();
      } catch (err) { app.error(err); }
    } else chosen = null;
  }
  function keys(e: KeyboardEvent) {
    const step = 60;
    if (e.key === 'ArrowLeft') view = { ...view, x: view.x + step };
    else if (e.key === 'ArrowRight') view = { ...view, x: view.x - step };
    else if (e.key === 'ArrowUp') view = { ...view, y: view.y + step };
    else if (e.key === 'ArrowDown') view = { ...view, y: view.y - step };
    else if (e.key === '+' || e.key === '=') zoomAt(1.4);
    else if (e.key === '-') zoomAt(1 / 1.4);
    else if (e.key === 'Escape') { placing = null; chosen = null; }
    else return;
    e.preventDefault();
  }
  function flyTo(p: Place) {
    touched = true;
    chosen = p.id;
    const { x, y } = project(p.lat, p.lng);
    const k = Math.max(view.k, 40);
    view = { k, x: size.w / 2 - x * k, y: size.h / 2 - y * k };
  }
  async function place() {
    const n = await app.pickNote('Place a note on the map', { confirmLabel: 'Choose' });
    if (n) { placing = { id: n.id, title: n.title }; app.toast(`Now click the map where “${n.title}” belongs.`, 'info'); }
  }
  async function clearPlace(p: Place) {
    if (!(await app.confirm(`Take “${p.title}” off the map?`, 'Its location property is removed; the note stays.', 'Remove location'))) return;
    try { await api.patch(`nodes/${p.id}`, { remove: ['location', 'coordinates', 'geo', 'latlng', 'lat', 'lng', 'lon', 'latitude', 'longitude'] }); chosen = null; await load(); } catch (e) { app.error(e); }
  }

  const colors: Record<string, string> = { task: 'var(--warn)', person: 'var(--ok)', project: '#c49bff', event: '#78d3e8', daily: 'var(--text-muted)' };
  const pos = (x: number, y: number) => `left:${view.x + x * view.k}px;top:${view.y + y * view.k}px`;
</script>

<div class="map-view">
  <div class="top">
    <h1><Icon name="map" size={20} /> Map</h1>
    <input class="input" aria-label="Filter places" placeholder="Filter by title, place or tag" bind:value={filter} />
    <select class="input" aria-label="Where" bind:value={everywhere}><option value={false}>This workspace</option><option value={true}>All workspaces</option></select>
    <button class="btn" onclick={place}><Icon name="plus" size={14} /> Place a note</button>
    <span class="grow"></span>
    <button class="icon-btn" aria-label="Zoom out" onclick={() => zoomAt(1 / 1.4)}>−</button>
    <button class="icon-btn" aria-label="Zoom in" onclick={() => zoomAt(1.4)}>+</button>
    <button class="btn ghost" onclick={() => { touched = false; fit(); }}>Fit</button>
  </div>
  {#if placing}<p class="placing"><Icon name="map" size={14} /> Click the map to place “{placing.title}”. <button class="btn ghost" onclick={() => (placing = null)}>Cancel</button></p>{/if}

  <div class="body">
    <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
    <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
    <div class="stage" class:placing={!!placing} bind:this={stage} tabindex="0" role="application" aria-label="World map: arrow keys pan, plus and minus zoom"
      onwheel={wheel} onpointerdown={down} onpointermove={move} onpointerup={up} onkeydown={keys}>
      <svg width={size.w} height={size.h} aria-hidden="true">
        <g transform={`translate(${view.x},${view.y}) scale(${view.k})`}>
          <rect x="0" y="0" width={WORLD} height={WORLD} class="sea" />
          {#each countries as d, i (i)}<path {d} class="land" style={`stroke-width:${0.6 / view.k}`} />{/each}
        </g>
      </svg>
      {#each groups as g, i (g.items[0].id + i)}
        {#if g.items.length > 1}
          <button class="cluster" style={pos(g.x, g.y)} aria-label={`${g.items.length} places here; zoom in`}
            onclick={() => fit(g.items)}>{g.items.length}</button>
        {:else}
          {@const p = g.items[0]}
          <button class="pin" class:on={chosen === p.id} style={`${pos(p.x, p.y)};--c:${colors[p.type] ?? 'var(--accent)'}`} aria-label={`${p.title}${p.name ? ', ' + p.name : ''}`} title={p.title}
            onclick={() => (chosen = p.id)}></button>
        {/if}
      {/each}
      {#if picked}
        {@const pp = project(picked.lat, picked.lng)}
        <div class="card" style={pos(pp.x, pp.y)}>
          <button class="title" onclick={(e) => openNode(picked.id, whereFrom(e) === 'current' ? 'new' : whereFrom(e), picked.title)}>{picked.title}</button>
          <div class="faint small">{picked.name ? picked.name + ' · ' : ''}{formatCoord(picked.lat, picked.lng)}</div>
          <div class="row"><button class="btn ghost" onclick={() => clearPlace(picked)}>Remove location</button></div>
        </div>
      {/if}
      {#if !countries.length}<div class="loading faint">Loading the map…</div>{/if}
      <span class="credit faint">Map: Natural Earth</span>
    </div>

    <aside class="list scroll" aria-label="Places">
      <h2 class="label">{shown.length} {shown.length === 1 ? 'place' : 'places'}</h2>
      {#each shown as p (p.id)}
        <button class="place" class:on={chosen === p.id} onclick={() => flyTo(p)} ondblclick={() => openNode(p.id, 'new', p.title)}>
          <span class="dot" style={`background:${colors[p.type] ?? 'var(--accent)'}`}></span>
          <span class="pt">{p.title}</span><span class="faint small">{p.name ?? ''}</span>
        </button>
      {:else}
        <p class="faint small">No notes with a location yet. Use <b>Place a note</b>, or give a note a <code>location</code> property like <code>51.5072, -0.1276</code>.</p>
      {/each}
    </aside>
  </div>
</div>

<style>
  .map-view { flex: 1; display: flex; flex-direction: column; min-height: 0; padding: 14px 18px 10px; gap: 8px; }
  .top { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  h1 { display: flex; align-items: center; gap: 8px; font: 700 22px/1.2 var(--font-display); margin-right: 6px; }
  .top .input { width: auto; }
  .grow { flex: 1; }
  .placing { display: flex; align-items: center; gap: 8px; margin: 0; color: var(--accent); font-size: 13px; }
  .body { flex: 1; min-height: 0; display: grid; grid-template-columns: 1fr 240px; gap: 10px; }
  .stage { position: relative; overflow: hidden; border: 1px solid var(--line); border-radius: 10px; background: var(--bg); cursor: grab; touch-action: none; outline: none; }
  .stage:focus-visible { border-color: var(--accent); }
  .stage.placing { cursor: crosshair; }
  svg { position: absolute; inset: 0; display: block; }
  .sea { fill: var(--bg); }
  .land { fill: color-mix(in srgb, var(--text) 10%, var(--bg-raised)); stroke: var(--line-strong); stroke-linejoin: round; }
  .pin { position: absolute; width: 14px; height: 14px; margin: -7px 0 0 -7px; border-radius: 50%; background: var(--c); border: 2px solid var(--bg); box-shadow: 0 1px 4px rgb(0 0 0 / .5); padding: 0; cursor: pointer; }
  .pin.on, .pin:focus-visible { outline: 2px solid var(--text); outline-offset: 1px; }
  .cluster { position: absolute; min-width: 26px; height: 26px; margin: -13px 0 0 -13px; border-radius: 13px; background: var(--accent); color: var(--accent-ink); font: 700 12px var(--font-mono); border: 2px solid var(--bg); cursor: pointer; padding: 0 6px; }
  .card { position: absolute; transform: translate(-50%, calc(-100% - 14px)); background: var(--bg-panel); border: 1px solid var(--line-strong); border-radius: 9px; padding: 8px 10px; box-shadow: var(--shadow); min-width: 180px; max-width: 260px; z-index: 2; }
  .card .title { border: 0; background: none; padding: 0; color: var(--text); font: 600 14px var(--font-body); cursor: pointer; text-align: left; }
  .card .title:hover { color: var(--accent); text-decoration: underline; }
  .row { margin-top: 6px; }
  .small { font-size: 12px; }
  .loading { position: absolute; inset: 0; display: grid; place-items: center; pointer-events: none; }
  .credit { position: absolute; right: 8px; bottom: 6px; font-size: 10.5px; pointer-events: none; }
  .list { border: 1px solid var(--line); border-radius: 10px; padding: 10px 8px; background: var(--bg-raised); }
  .list h2 { margin: 0 6px 6px; }
  .place { display: grid; grid-template-columns: 10px 1fr; column-gap: 8px; width: 100%; text-align: left; border: 0; background: none; color: var(--text-soft); padding: 6px; border-radius: 6px; cursor: pointer; font-size: 13.5px; }
  .place .small { grid-column: 2; }
  .place:hover, .place.on { background: var(--bg-hover); color: var(--text); }
  .dot { width: 9px; height: 9px; border-radius: 50%; margin-top: 5px; }
  .pt { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  @container pane (max-width: 760px) {
    .body { grid-template-columns: 1fr; grid-template-rows: 1fr 160px; }
  }
</style>
