/// <reference types="svelte" />
/// <reference types="vite/client" />

/** The build mark, set when the package is built for testing (build/build.ps1 -Mark). Empty in a released build. */
declare const __QUANTOS_BUILD__: string;

interface Window {
  chrome?: { webview?: { postMessage(message: unknown): void; addEventListener(type: 'message', listener: (e: MessageEvent) => void): void } };
}
