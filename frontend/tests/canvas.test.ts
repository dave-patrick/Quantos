import { describe, expect, it } from 'vitest';
import { bounds, contained, edgePath, facing, parseCanvas, serializeCanvas } from '../src/lib/canvas';

describe('canvas', () => {
  it('reads and writes the JSON Canvas block, keeping text around it', () => {
    const body = 'Planning board.\n\n```canvas\n{"nodes":[{"id":"a","type":"text","x":1.4,"y":2,"width":200,"height":80,"text":"Hi"},{"id":"b","type":"file","x":300,"y":0,"width":240,"height":120,"node":"01JQEXAMP0E000000000000030","file":"workspaces/default/nodes/apollo_launch.md"}],"edges":[{"id":"e","fromNode":"a","toNode":"b"},{"id":"x","fromNode":"a","toNode":"gone"}]}\n```\n';
    const { canvas, error } = parseCanvas(body);
    expect(error).toBeNull();
    expect(canvas.nodes).toHaveLength(2);
    expect(canvas.edges.map((e) => e.id)).toEqual(['e']); // the edge to a missing card is dropped
    const out = serializeCanvas(body, canvas);
    expect(out.startsWith('Planning board.\n\n```canvas\n')).toBe(true);
    expect(parseCanvas(out).canvas.nodes[0].x).toBe(1);
  });

  it('adds a block to a body without one, and survives bad JSON', () => {
    expect(serializeCanvas('', { nodes: [], edges: [] })).toBe('```canvas\n{\n "nodes": [],\n "edges": []\n}\n```\n');
    expect(parseCanvas('```canvas\n{oops\n```').error).toBeTruthy();
  });

  it('works out geometry', () => {
    const a = { x: 0, y: 0, width: 100, height: 50 }, b = { x: 300, y: 10, width: 100, height: 50 };
    expect(facing(a, b)).toEqual(['right', 'left']);
    expect(facing(a, { x: 0, y: 200, width: 10, height: 10 })).toEqual(['bottom', 'top']);
    expect(edgePath({ x: 100, y: 25 }, 'right', { x: 300, y: 35 }, 'left')).toMatch(/^M100,25 C/);
    expect(bounds([a, b])).toEqual({ x: 0, y: 0, width: 400, height: 60 });
    expect(contained({ x: -10, y: -10, width: 500, height: 100 }, a)).toBe(true);
  });
});
