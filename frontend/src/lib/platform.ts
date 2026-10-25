/** True inside the Windows client (WebView2), which provides native features through a narrow bridge. */
export const isWindowsClient = typeof window !== 'undefined' && !!window.chrome?.webview;
export const isAndroidClient = typeof navigator !== 'undefined' && /QuantosAndroid\//.test(navigator.userAgent);
export const isNativeClient = isWindowsClient || isAndroidClient;

/** Messages the page may send to the Windows client. The client checks each one; nothing here grants native access by itself. */
export type BridgeMessage = { type: string; [key: string]: unknown };

type BridgeListener = (message: any) => void;
const listeners = new Set<BridgeListener>();

if (isWindowsClient) {
  window.chrome!.webview!.addEventListener('message', (e) => listeners.forEach((l) => l(e.data)));
}

export const bridge = {
  send(message: BridgeMessage) {
    if (isWindowsClient) window.chrome!.webview!.postMessage(message);
    else if (isAndroidClient) location.href = 'quantos://native?message=' + encodeURIComponent(JSON.stringify(message));
  },
  on(listener: BridgeListener) {
    listeners.add(listener);
    return () => listeners.delete(listener);
  }
};

if (isAndroidClient) {
  window.addEventListener('quantos-native', (e) => listeners.forEach((l) => l((e as CustomEvent).detail)));
  document.documentElement.dataset.platform = 'android';
}

export const isMac = typeof navigator !== 'undefined' && /Mac/.test(navigator.platform);
export const modKey = isMac ? '⌘' : 'Ctrl';
