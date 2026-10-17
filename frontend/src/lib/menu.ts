/**
 * Keyboard behaviour for a popup menu: the first item gets focus, Up/Down/Home/End move between
 * items, Escape (or Tab) closes it, and focus goes back to whatever opened it.
 * Use as <div role="menu" use:menu={{ onclose }}> with role="menuitem" buttons inside.
 */
export function menu(node: HTMLElement, options: { onclose: () => void }) {
  let opts = options;
  const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  const items = () => [...node.querySelectorAll<HTMLElement>('[role^=menuitem]:not([disabled])')];
  queueMicrotask(() => items()[0]?.focus());

  function onkeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); opts.onclose(); return; }
    const list = items();
    const i = list.indexOf(document.activeElement as HTMLElement);
    if (i < 0) return; // typing in a field inside the menu: leave its keys alone
    const go = (n: number) => { e.preventDefault(); list[(n + list.length) % list.length]?.focus(); };
    if (e.key === 'ArrowDown') go(i + 1);
    else if (e.key === 'ArrowUp') go(i - 1);
    else if (e.key === 'Home') go(0);
    else if (e.key === 'End') go(list.length - 1);
    else if (e.key === 'Tab') { e.preventDefault(); opts.onclose(); }
  }

  node.addEventListener('keydown', onkeydown);
  return {
    update(next: { onclose: () => void }) { opts = next; },
    destroy() {
      node.removeEventListener('keydown', onkeydown);
      queueMicrotask(() => { if (opener?.isConnected && (!document.activeElement || document.activeElement === document.body)) opener.focus(); });
    }
  };
}
