/// <reference types="svelte" />
/// <reference types="vite/client" />

interface Window {
  chrome?: { webview?: { postMessage(message: unknown): void; addEventListener(type: 'message', listener: (e: MessageEvent) => void): void } };
}
