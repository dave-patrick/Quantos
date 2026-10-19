// Tiny CDP driver for headless Edge: eval | shot | nav | key | type
import { writeFileSync } from 'node:fs';
setTimeout(() => { console.log('timeout'); process.exit(0); }, 25000).unref?.();
const [port, cmd, ...args] = process.argv.slice(2);
const targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
const page = targets.filter((t) => t.type === 'page').find((t) => t.url.includes('5173')) ?? targets.find((t) => t.type === 'page');
const ws = new WebSocket(page.webSocketDebuggerUrl);
let id = 0; const waiting = new Map();
ws.onmessage = (m) => { const msg = JSON.parse(m.data); if (msg.id && waiting.has(msg.id)) { waiting.get(msg.id)(msg); waiting.delete(msg.id); } };
await new Promise((r) => (ws.onopen = r));
const send = (method, params = {}) => new Promise((resolve, reject) => { const i = ++id; waiting.set(i, (m) => (m.error ? reject(new Error(m.error.message)) : resolve(m.result))); ws.send(JSON.stringify({ id: i, method, params })); });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const keyInfo = { Enter: [13, '\r'], Escape: [27, ''], Tab: [9, ''], Backspace: [8, ''], ArrowDown: [40, ''], ArrowUp: [38, ''], ArrowLeft: [37, ''], ArrowRight: [39, ''], Delete: [46, ''], Home: [36, ''], End: [35, ''] };
try {
  if (cmd === 'eval') {
    const r = await send('Runtime.evaluate', { expression: args[0], awaitPromise: true, returnByValue: true });
    console.log(r.exceptionDetails ? 'EXCEPTION: ' + (r.exceptionDetails.exception?.description ?? r.exceptionDetails.text) : JSON.stringify(r.result.value, null, 1));
  } else if (cmd === 'shot') {
    const r = await send('Page.captureScreenshot', { format: 'png' });
    writeFileSync(args[0], Buffer.from(r.data, 'base64')); console.log('saved');
  } else if (cmd === 'nav') {
    await send('Page.navigate', { url: args[0] }); await sleep(2500); console.log('ok');
  } else if (cmd === 'key') {
    // key <name> [modifiers: 1 alt, 2 ctrl, 4 meta, 8 shift] [repeat]
    const key = args[0]; const mods = Number(args[1] ?? 0); const times = Number(args[2] ?? 1);
    const [code, text] = keyInfo[key] ?? [key.toUpperCase().charCodeAt(0), key];
    for (let n = 0; n < times; n++) {
      await send('Input.dispatchKeyEvent', { type: 'keyDown', key, code: key.length === 1 ? 'Key' + key.toUpperCase() : key, windowsVirtualKeyCode: code, modifiers: mods, text: mods & 3 ? undefined : text || undefined });
      await send('Input.dispatchKeyEvent', { type: 'keyUp', key, windowsVirtualKeyCode: code, modifiers: mods });
    }
    await sleep(300); console.log('ok');
  } else if (cmd === 'click') {
    const [x, y] = [Number(args[0]), Number(args[1])];
    for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x, y, button: 'left', clickCount: 1 });
    await sleep(300); console.log('ok');
  } else if (cmd === 'drag') {
    const [x1, y1, x2, y2] = args.map(Number);
    await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: x1, y: y1 });
    await send('Input.dispatchMouseEvent', { type: 'mousePressed', x: x1, y: y1, button: 'left', buttons: 1, clickCount: 1 });
    for (let i = 1; i <= 8; i++) await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: x1 + ((x2 - x1) * i) / 8, y: y1 + ((y2 - y1) * i) / 8, button: 'left', buttons: 1 });
    await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: x2, y: y2, button: 'left', buttons: 0, clickCount: 1 });
    await sleep(300); console.log('ok');
  } else if (cmd === 'type') {
    await send('Input.insertText', { text: args[0] }); await sleep(300); console.log('ok');
  }
} catch (e) { console.log('ERROR', e.message); }
process.exit(0);
