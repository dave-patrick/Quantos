import { describe, expect, it } from 'vitest';
import { cluster, project, ringsToPath, topoRings, unproject, WORLD } from '../src/lib/geo';

describe('geo', () => {
  it('projects and unprojects', () => {
    expect(project(0, 0)).toEqual({ x: WORLD / 2, y: WORLD / 2 });
    const london = project(51.5072, -0.1276);
    const back = unproject(london.x, london.y);
    expect(back.lat).toBeCloseTo(51.5072, 4);
    expect(back.lng).toBeCloseTo(-0.1276, 4);
    expect(project(89, 0).y).toBeCloseTo(0, 3); // clamped near the poles
  });

  it('splits rings that cross the date line', () => {
    const d = ringsToPath([[[179, 0], [-179, 1], [-178, 2]]]);
    expect(d.match(/M/g)).toHaveLength(2);
  });

  it('decodes shared, delta-encoded TopoJSON arcs', () => {
    const topo = { transform: { scale: [1, 1] as [number, number], translate: [0, 0] as [number, number] }, arcs: [[[0, 0], [10, 0], [0, 10]], [[10, 10], [-10, 0], [0, -10]]],
      objects: { countries: { type: 'GeometryCollection', geometries: [{ type: 'Polygon', arcs: [[0, 1]] }, { type: 'MultiPolygon', arcs: [[[~1]]] }] } } };
    const [square, reversed] = topoRings(topo, 'countries');
    expect(square[0]).toEqual([[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]]);
    expect(reversed[0]).toEqual([[0, 0], [0, 10], [10, 10]]);
  });

  it('clusters pins that overlap on screen', () => {
    const pins = [{ id: 'a', x: 100, y: 100 }, { id: 'b', x: 101, y: 100 }, { id: 'c', x: 300, y: 300 }];
    expect(cluster(pins, 1).map((c) => c.items.length)).toEqual([2, 1]);
    expect(cluster(pins, 100)).toHaveLength(3); // zoomed in, they separate
  });
});
