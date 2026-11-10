// @vitest-environment happy-dom
import { afterEach, expect, it, vi } from 'vitest';
import { plugins } from '../src/lib/plugins.svelte';

afterEach(() => vi.unstubAllGlobals());

it('allows encoded search text but blocks encoded traversal and privileged endpoints', async () => {
  const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => [] });
  vi.stubGlobal('fetch', fetchMock);
  const source = { postMessage: vi.fn() };
  const bridge = plugins as unknown as {
    nameOf: (source: unknown) => string;
    onMessage: (event: unknown) => Promise<void>;
  };
  const name = vi.spyOn(bridge, 'nameOf').mockReturnValue('test-plugin');
  try {
    for (const path of ['search?q=hello%20world', 'search?q=%E6%97%A5%E6%9C%AC', 'nodes/%2e%2e/system', 'system/status', 'nodes/%252e%252e/system']) {
      await bridge.onMessage({ source, data: { quantos: true, id: 1, method: 'api', args: { path } } });
    }
    expect(fetchMock.mock.calls.map(([path]) => path)).toEqual([
      '/api/search?q=hello%20world', '/api/search?q=%E6%97%A5%E6%9C%AC'
    ]);
  } finally { name.mockRestore(); }
});
