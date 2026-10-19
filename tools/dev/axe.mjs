// Accessibility scan with axe-core in the headless Edge on 9333 (started by tools/dev/start.ps1).
//   node tools/dev/axe.mjs here                 scans the page as it is
//   node tools/dev/axe.mjs Notes Gantt Graph    clicks each left-sidebar entry by its label, then scans
// Prints the open tab and any serious or critical violations per view; an empty list means none.
import { readFileSync } from 'node:fs';
const axe = readFileSync(new URL('../../frontend/node_modules/axe-core/axe.min.js', import.meta.url), 'utf8');
const targets = await (await fetch('http://127.0.0.1:9333/json')).json();
const page = targets.filter((t) => t.type === 'page').find((t) => /5173|47821/.test(t.url)) ?? targets.find((t) => t.type === 'page');
const ws = new WebSocket(page.webSocketDebuggerUrl);
let id = 0; const waiting = new Map();
ws.onmessage = (m) => { const msg = JSON.parse(m.data); if (msg.id && waiting.has(msg.id)) { waiting.get(msg.id)(msg); waiting.delete(msg.id); } };
await new Promise((r) => (ws.onopen = r));
const send = (method, params = {}) => new Promise((resolve) => { const i = ++id; waiting.set(i, resolve); ws.send(JSON.stringify({ id: i, method, params })); });
const run = async (expression) => (await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true })).result;
await run(axe);
let failed = false;
for (const name of process.argv.slice(2).length ? process.argv.slice(2) : ['here']) {
  const r = await run(`(async () => {
    if (${JSON.stringify(name)} !== 'here') [...document.querySelectorAll('nav button, aside button')].find((b) => b.textContent.trim() === ${JSON.stringify(name)})?.click();
    await new Promise((r) => setTimeout(r, 1600));
    const res = await axe.run(document, { resultTypes: ['violations'] });
    const tab = document.querySelector('[role=tab][aria-selected=true]')?.textContent?.trim();
    return { tab, problems: res.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id + ': ' + v.nodes.slice(0, 4).map((n) => n.target.join(' ')).join(' | ')) };
  })()`);
  const v = r.result?.value ?? { tab: '?', problems: ['scan failed: ' + JSON.stringify(r).slice(0, 200)] };
  if (v.problems.length) failed = true;
  console.log(`${name} (${v.tab}): ${v.problems.length ? '\n  ' + v.problems.join('\n  ') : 'no serious or critical issues'}`);
}
process.exit(failed ? 1 : 0);
