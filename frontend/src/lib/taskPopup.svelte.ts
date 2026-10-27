/** Keep the workspace mounted and restore its viewport after native dialog focus restoration. */
export const taskPopup = $state<{ id: string | null; returnTo: (() => void) | null }>({ id: null, returnTo: null });

export function openTaskPopup(id: string, origin: HTMLElement | null) {
  const editor = origin?.closest('.cm-editor')?.querySelector<HTMLElement>('.cm-content');
  const opener = editor ?? (document.activeElement instanceof HTMLElement ? document.activeElement : null);
  const positions: { element: HTMLElement; top: number; left: number }[] = [];
  for (let element = origin; element; element = element.parentElement) {
    if (element.scrollHeight > element.clientHeight || element.scrollWidth > element.clientWidth)
      positions.push({ element, top: element.scrollTop, left: element.scrollLeft });
  }
  // Nested tasks retain the original note as their return destination.
  if (!taskPopup.id) taskPopup.returnTo = () => {
    opener?.focus({ preventScroll: true });
    for (const { element, top, left } of positions) if (element.isConnected) {
      element.scrollTop = top; element.scrollLeft = left;
    }
  };
  taskPopup.id = id;
}
