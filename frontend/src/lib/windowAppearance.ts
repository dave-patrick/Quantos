import { bridge, isWindowsClient } from './platform';

/** Resolve the final CSS palette, including custom theme files, for the native frame. */
export function watchWindowAppearance(): () => void {
  if (!isWindowsClient) return () => {};
  let frame = 0;
  let previous = '';
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 1;
  const context = canvas.getContext('2d', { willReadFrequently: true })!;
  const send = () => {
    frame = 0;
    const css = getComputedStyle(document.documentElement);
    const colors: Record<string, string> = {};
    for (const [key, token] of Object.entries({ background: '--bg', foreground: '--text-soft', text: '--text', hover: '--bg-hover', active: '--bg-active' })) {
      const color = css.getPropertyValue(token).trim();
      if (!CSS.supports('color', color)) return;
      // Canvas converts modern CSS colors to sRGB and blends translucent tokens over the frame.
      context.fillStyle = colors.background ?? (document.documentElement.dataset.theme === 'light' ? '#f5f7fb' : '#2a2b2d');
      context.fillRect(0, 0, 1, 1);
      context.fillStyle = color;
      context.fillRect(0, 0, 1, 1);
      colors[key] = '#' + [...context.getImageData(0, 0, 1, 1).data].slice(0, 3).map((n) => n.toString(16).padStart(2, '0')).join('');
    }
    const signature = JSON.stringify(colors);
    if (signature !== previous) {
      previous = signature;
      bridge.send({ type: 'appearance', ...colors });
    }
  };
  const schedule = () => { if (!frame) frame = requestAnimationFrame(send); };
  const root = new MutationObserver(schedule);
  root.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'style', 'class'] });
  const head = new MutationObserver(schedule);
  head.observe(document.head, { childList: true, subtree: true, characterData: true, attributes: true });
  document.head.addEventListener('load', schedule, true);
  schedule();
  return () => { root.disconnect(); head.disconnect(); document.head.removeEventListener('load', schedule, true); cancelAnimationFrame(frame); };
}
