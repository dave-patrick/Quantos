/**
 * Keyboard behaviour for a dialog element: Tab stays inside it, Escape closes it, and when it goes
 * away focus returns to whatever had it before (the button that opened it, usually).
 * Use as <div role="dialog" use:modal={{ onclose }}>.
 */
export function modal(node: HTMLElement, options: { onclose: () => void }) {
  let opts = options;
  const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;

  function onkeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); opts.onclose(); return; }
    if (e.key !== 'Tab') return;
    const items = [...node.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled]), select, textarea, [tabindex]:not([tabindex="-1"])')];
    if (!items.length) return;
    const i = items.indexOf(document.activeElement as HTMLElement);
    const next = e.shiftKey ? (i <= 0 ? items.length - 1 : i - 1) : (i === -1 || i === items.length - 1 ? 0 : i + 1);
    e.preventDefault();
    items[next].focus();
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
