// @vitest-environment happy-dom
import { describe, expect, it, vi } from 'vitest';
import { menu } from '../src/lib/menu';
import { modal } from '../src/lib/modal';

const tick = () => new Promise((r) => setTimeout(r, 0));
const press = (el: Element, key: string, init: KeyboardEventInit = {}) =>
  el.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init }));

function setup(html: string) {
  document.body.innerHTML = `<button id="launcher">open</button>${html}`;
  (document.getElementById('launcher') as HTMLButtonElement).focus();
}

describe('menu action', () => {
  it('focuses the first item, moves with arrows, closes on Escape and returns focus', async () => {
    setup('<div id="m" role="menu"><button role="menuitem">One</button><button role="menuitemradio">Two</button><button role="menuitem">Three</button></div>');
    const node = document.getElementById('m')!;
    const onclose = vi.fn();
    const action = menu(node, { onclose });
    await tick();
    expect(document.activeElement?.textContent).toBe('One');
    press(node, 'ArrowDown');
    expect(document.activeElement?.textContent).toBe('Two');
    press(node, 'ArrowUp'); press(node, 'ArrowUp');
    expect(document.activeElement?.textContent).toBe('Three'); // wraps around
    press(node, 'Home');
    expect(document.activeElement?.textContent).toBe('One');
    press(node, 'Escape');
    expect(onclose).toHaveBeenCalledOnce();
    node.remove(); action.destroy(); await tick();
    expect(document.activeElement?.id).toBe('launcher');
  });

  it('leaves keys alone while typing in a field inside the menu', async () => {
    setup('<div id="m" role="menu"><button role="menuitem">One</button><input id="f" /></div>');
    const node = document.getElementById('m')!;
    const onclose = vi.fn();
    menu(node, { onclose });
    await tick();
    const field = document.getElementById('f') as HTMLInputElement;
    field.focus();
    press(field, 'Tab');
    press(field, 'ArrowDown');
    expect(onclose).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(field);
    press(field, 'Escape');
    expect(onclose).toHaveBeenCalledOnce();
  });
});

describe('modal action', () => {
  it('keeps Tab inside the dialog, closes on Escape and returns focus', async () => {
    setup('<div id="d" role="dialog"><textarea id="a"></textarea><button id="b">Save</button></div>');
    const node = document.getElementById('d')!;
    const onclose = vi.fn();
    const action = modal(node, { onclose });
    const last = document.getElementById('b') as HTMLButtonElement;
    last.focus();
    press(last, 'Tab');
    expect(document.activeElement?.id).toBe('a'); // wraps to the first control
    press(document.activeElement!, 'Tab', { shiftKey: true });
    expect(document.activeElement?.id).toBe('b');
    press(document.activeElement!, 'Escape');
    expect(onclose).toHaveBeenCalledOnce();
    node.remove(); action.destroy(); await tick();
    expect(document.activeElement?.id).toBe('launcher');
  });
});
