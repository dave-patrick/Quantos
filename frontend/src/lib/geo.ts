/**
 * Map maths for the location view (Release 1.2): Web Mercator on a square world `size` units wide,
 * and clustering pins that would overlap on screen.
 */
export const WORLD = 1024;
const MAX_LAT = 85.05112878;

export function project(lat: number, lng: number, size = WORLD): { x: number; y: number } {
  const φ = (Math.max(-MAX_LAT, Math.min(MAX_LAT, lat)) * Math.PI) / 180;
  return { x: ((lng + 180) / 360) * size, y: ((1 - Math.log(Math.tan(φ) + 1 / Math.cos(φ)) / Math.PI) / 2) * size };
}

export function unproject(x: number, y: number, size = WORLD): { lat: number; lng: number } {
  const lng = (x / size) * 360 - 180;
  const n = Math.PI - (2 * Math.PI * y) / size;
  const lat = (180 / Math.PI) * Math.atan(0.5 * (Math.exp(n) - Math.exp(-n)));
  return { lat, lng: ((((lng + 180) % 360) + 360) % 360) - 180 };
}

/** Rings of [lng, lat] points as one SVG path in world units (rings crossing the date line are split). */
export function ringsToPath(rings: number[][][]): string {
  let d = '';
  for (const ring of rings) {
    let prev: number | null = null;
    ring.forEach(([lng, lat], i) => {
      const p = project(lat, lng);
      const jump = prev !== null && Math.abs(lng - prev) > 180;
      d += `${i === 0 || jump ? 'M' : 'L'}${p.x.toFixed(1)},${p.y.toFixed(1)}`;
      prev = lng;
    });
    d += 'Z';
  }
  return d;
}

export interface Pin { id: string; x: number; y: number }
export interface Cluster<T extends Pin> { x: number; y: number; items: T[] }

/** Greedy clustering in screen pixels: pins within `radius` of a cluster's first pin join it. */
export function cluster<T extends Pin>(pins: T[], zoom: number, radius = 22): Cluster<T>[] {
  const out: Cluster<T>[] = [];
  for (const p of [...pins].sort((a, b) => a.x - b.x || a.y - b.y)) {
    const hit = out.find((c) => Math.hypot((c.items[0].x - p.x) * zoom, (c.items[0].y - p.y) * zoom) < radius);
    if (hit) { hit.items.push(p); hit.x = hit.items.reduce((s, i) => s + i.x, 0) / hit.items.length; hit.y = hit.items.reduce((s, i) => s + i.y, 0) / hit.items.length; }
    else out.push({ x: p.x, y: p.y, items: [p] });
  }
  return out;
}

export const formatCoord = (lat: number, lng: number) => `${lat.toFixed(5)}, ${lng.toFixed(5)}`;

/** Just enough TopoJSON to draw country outlines: arcs are shared, delta-encoded and quantized. */
interface Topology { transform?: { scale: [number, number]; translate: [number, number] }; arcs: number[][][]; objects: Record<string, { type: string; geometries?: Geometry[] }> }
interface Geometry { type: string; arcs?: number[][] | number[][][] }

export function topoRings(topo: Topology, object: string): number[][][][] {
  const [sx, sy] = topo.transform?.scale ?? [1, 1];
  const [tx, ty] = topo.transform?.translate ?? [0, 0];
  const arcs = topo.arcs.map((arc) => {
    let x = 0, y = 0;
    return arc.map(([dx, dy]) => (topo.transform ? (x += dx, y += dy, [x * sx + tx, y * sy + ty]) : [dx, dy]));
  });
  const ring = (ids: number[]) => ids.flatMap((i, n) => {
    const pts = i >= 0 ? arcs[i] : [...arcs[~i]].reverse();
    return n ? pts.slice(1) : pts;
  });
  const polygons: number[][][][] = [];
  for (const g of topo.objects[object]?.geometries ?? []) {
    if (g.type === 'Polygon') polygons.push((g.arcs as number[][]).map(ring));
    else if (g.type === 'MultiPolygon') for (const p of g.arcs as number[][][]) polygons.push(p.map(ring));
  }
  return polygons;
}
