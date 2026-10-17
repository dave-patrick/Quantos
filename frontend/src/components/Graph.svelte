<script lang="ts">
  import { api } from '../lib/api';
  import { onChange } from '../lib/events';
  import { openNode } from '../lib/links.svelte';
  import { onMount } from 'svelte';

  interface GNode { id: string; title: string; type: string; workspace: string; depth: number; x: number; y: number; vx: number; vy: number }
  interface GEdge { from: string; to: string; kind: string }

  let { centerId = null, workspace = null, hops = 2, height = 420, labels = true }:
    { centerId?: string | null; workspace?: string | null; hops?: number; height?: number; labels?: boolean } = $props();

  let nodes = $state<GNode[]>([]);
  let edges = $state<GEdge[]>([]);
  let width = $state(600);
  let view = $state({ x: 0, y: 0, k: 1 });
  let hovered = $state<string | null>(null);
  let box = $state<HTMLDivElement | null>(null);
  let timer: ReturnType<typeof setTimeout>;

  async function load() {
    const q = centerId ? `graph?id=${centerId}&hops=${hops}` : `graph${workspace ? `?workspace=${workspace}` : ''}`;
    try {
      const data = await api.get<{ nodes: Omit<GNode, 'x' | 'y' | 'vx' | 'vy'>[]; edges: GEdge[] }>(q);
      const prev = new Map(nodes.map((n) => [n.id, n]));
      const placed = data.nodes.map((n, i) => {
        const p = prev.get(n.id);
        const a = (i / Math.max(1, data.nodes.length)) * Math.PI * 2;
        const r = n.depth * 110 + 20;
        return { ...n, x: p?.x ?? Math.cos(a) * r, y: p?.y ?? Math.sin(a) * r, vx: 0, vy: 0 };
      });
      simulate(placed, data.edges);
      nodes = placed;
      edges = data.edges;
      if (fitNext) { fitNext = false; fit(); }
    } catch { nodes = []; edges = []; }
  }

  /** A few hundred steps of a basic force layout: repulsion, springs along links, pull to the centre. */
  function simulate(ns: GNode[], es: GEdge[]) {
    const byId = new Map(ns.map((n) => [n.id, n]));
    for (let step = 0; step < 260; step++) {
      const cool = 1 - step / 260;
      for (let i = 0; i < ns.length; i++) {
        const a = ns[i];
        for (let j = i + 1; j < ns.length; j++) {
          const b = ns[j];
          let dx = a.x - b.x, dy = a.y - b.y;
          const d2 = dx * dx + dy * dy + 0.01;
          const f = 2200 / d2;
          dx *= f; dy *= f;
          a.vx += dx; a.vy += dy; b.vx -= dx; b.vy -= dy;
        }
      }
      for (const e of es) {
        const a = byId.get(e.from), b = byId.get(e.to);
        if (!a || !b) continue;
        const dx = b.x - a.x, dy = b.y - a.y;
        const d = Math.sqrt(dx * dx + dy * dy) || 1;
        const f = (d - 90) * 0.02;
        a.vx += (dx / d) * f; a.vy += (dy / d) * f; b.vx -= (dx / d) * f; b.vy -= (dy / d) * f;
      }
      for (const n of ns) {
        n.vx -= n.x * 0.03; n.vy -= n.y * 0.03;
        if (n.id === centerId) { n.x = 0; n.y = 0; n.vx = 0; n.vy = 0; continue; }
        n.x += Math.max(-20, Math.min(20, n.vx)) * cool;
        n.y += Math.max(-20, Math.min(20, n.vy)) * cool;
        n.vx *= 0.6; n.vy *= 0.6;
      }
    }
  }

  /** Frames every node (with room for labels), never zooming in past 1.5×. */
  function fit() {
    if (!nodes.length) { view = { x: 0, y: 0, k: 1 }; return; }
    const xs = nodes.map((n) => n.x), ys = nodes.map((n) => n.y);
    const [minX, maxX, minY, maxY] = [Math.min(...xs) - 90, Math.max(...xs) + 90, Math.min(...ys) - 30, Math.max(...ys) + 20];
    const k = Math.max(0.15, Math.min(1.5, width / (maxX - minX), height / (maxY - minY)));
    view = { x: -((minX + maxX) / 2) * k, y: -((minY + maxY) / 2) * k, k };
  }

  onMount(() => {
    const ro = new ResizeObserver(() => { if (box) width = box.clientWidth; });
    if (box) ro.observe(box);
    const off = onChange(() => { clearTimeout(timer); timer = setTimeout(load, 800); });
    return () => { ro.disconnect(); off(); };
  });

  let fitNext = true;
  $effect(() => { void centerId; void hops; void workspace; fitNext = true; load(); });

  const byId = $derived(new Map(nodes.map((n) => [n.id, n])));
  // Labels keep the same size on screen at any zoom; dots shrink and grow only a little.
  const inv = $derived(1 / view.k);
  const dot = $derived(1 / Math.sqrt(view.k));
  const neighbours = $derived(hovered ? new Set(edges.filter((e) => e.from === hovered || e.to === hovered).flatMap((e) => [e.from, e.to])) : null);

  let drag: { x: number; y: number } | null = null;
  function wheel(e: WheelEvent) { e.preventDefault(); view.k = Math.max(0.3, Math.min(3, view.k * (e.deltaY < 0 ? 1.1 : 0.9))); }
  function down(e: PointerEvent) { drag = { x: e.clientX - view.x, y: e.clientY - view.y }; }
  function move(e: PointerEvent) { if (drag) { view.x = e.clientX - drag.x; view.y = e.clientY - drag.y; } }
  const colors: Record<string, string> = { task: 'var(--warn)', daily: 'var(--text-muted)', person: 'var(--ok)', project: '#c49bff', supertag: '#f4b35e', synced_block: 'var(--ok)' };
</script>

<div class="graph" bind:this={box} style="height:{height}px">
  {#if nodes.length <= 1}
    <p class="faint empty">{centerId ? 'No links to or from this note yet.' : 'No linked notes yet.'}</p>
  {/if}
  <svg {width} {height} role="img" aria-label="Link graph" onwheel={wheel} onpointerdown={down} onpointermove={move} onpointerup={() => (drag = null)} onpointerleave={() => (drag = null)}>
    <g transform="translate({width / 2 + view.x},{height / 2 + view.y}) scale({view.k})">
      {#each edges as e, i (i)}
        {@const a = byId.get(e.from)}{@const b = byId.get(e.to)}
        {#if a && b}
          <line x1={a.x} y1={a.y} x2={b.x} y2={b.y} class="edge {e.kind}" class:dim={neighbours && !(neighbours.has(e.from) && neighbours.has(e.to))} />
        {/if}
      {/each}
      {#each nodes as n (n.id)}
        <g class="node" class:center={n.id === centerId} class:dim={neighbours && !neighbours.has(n.id)} transform="translate({n.x},{n.y})"
          role="button" tabindex="0" aria-label={n.title}
          onpointerdown={(e) => e.stopPropagation()}
          onclick={(e) => openNode(n.id, e.shiftKey ? 'other' : e.ctrlKey || e.metaKey ? 'new' : 'current', n.title)}
          onkeydown={(e) => { if (e.key === 'Enter') openNode(n.id, 'current', n.title); }}
          onmouseenter={() => (hovered = n.id)} onmouseleave={() => (hovered = null)}>
          <circle r={(n.id === centerId ? 9 : n.depth === 1 ? 6 : 4.5) * dot} style="fill:{colors[n.type] ?? 'var(--accent)'}" />
          {#if labels || n.id === hovered || n.id === centerId || n.depth <= 1}
            <text y={-12 * dot} text-anchor="middle" style="font-size:{(n.id === centerId ? 12.5 : 11.5) * inv}px">{n.title.length > 28 ? n.title.slice(0, 27) + '…' : n.title}</text>
          {/if}
        </g>
      {/each}
    </g>
  </svg>
</div>

<style>
  .graph { position: relative; overflow: hidden; border-radius: 10px; background: radial-gradient(circle at 50% 50%, var(--bg-raised), var(--bg)); }
  svg { display: block; cursor: grab; }
  svg:active { cursor: grabbing; }
  .edge { stroke: var(--line-strong); stroke-width: 1.2; }
  .edge.embed { stroke-dasharray: 4 3; }
  .edge.sync { stroke: var(--ok); }
  .node { cursor: pointer; }
  .node text { font-family: var(--font-body); fill: var(--text-soft); pointer-events: none; }
  .node.center text { fill: var(--text); font-weight: 700; }
  .node.center circle { stroke: var(--text); stroke-width: 2; }
  .node:hover circle, .node:focus circle { stroke: var(--text); stroke-width: 2; }
  .dim { opacity: .18; }
  .empty { position: absolute; inset: 0; display: grid; place-items: center; margin: 0; font-size: 13px; pointer-events: none; }
</style>
