/** True inside the Windows client (WebView2), which provides native features through a narrow bridge. */
export const isWindowsClient = typeof window !== 'undefined' && !!window.chrome?.webview;

export type BridgeMessage =
  | { type: 'ready' }
  | { type: 'notify'; title: string; body: string; tag?: string }
  | { type: 'open-scratchpad' }
  | { type: 'open-external'; url: string }
  | { type: 'shortcuts-changed' }
  | { type: 'terminal'; action: 'open' | 'input' | 'resize' | 'close'; id?: string; data?: string; cols?: number; rows?: number }
  | { type: 'exit' }
  | { type: 'pending-saves'; count: number };

type BridgeListener = (message: any) => void;
const listeners = new Set<BridgeListener>();

if (isWindowsClient) {
  window.chrome!.webview!.addEventListener('message', (e) => listeners.forEach((l) => l(e.data)));
}

export const bridge = {
  send(message: BridgeMessage) {
    if (isWindowsClient) window.chrome!.webview!.postMessage(message);
  },
  on(listener: BridgeListener) {
    listeners.add(listener);
    return () => listeners.delete(listener);
  }
};

export const isMac = typeof navigator !== 'undefined' && /Mac/.test(navigator.platform);
export const modKey = isMac ? '⌘' : 'Ctrl';
