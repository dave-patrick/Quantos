import { bridge, isWindowsClient } from './platform';

/**
 * What the Windows client reports about itself: its global shortcuts as actually registered, and any it
 * couldn't register (another program owns them). Kept here so pages opened later still know.
 */
class ClientInfo {
  captureHotkey = $state<string | null>(null);
  scratchpadHotkey = $state<string | null>(null);
  problems = $state<string[]>([]);

  constructor() {
    if (!isWindowsClient) return;
    bridge.on((m) => {
      if (m?.type === 'client') {
        this.captureHotkey = m.captureHotkey ?? null;
        this.scratchpadHotkey = m.scratchpadHotkey ?? null;
        this.problems = (m.hotkeyProblems ?? []).filter(Boolean);
      }
      if (m?.type === 'hotkey-status') this.problems = (m.problems ?? []).filter(Boolean);
    });
  }

  refresh() { if (isWindowsClient) bridge.send({ type: 'client-info' }); }
}

export const clientInfo = new ClientInfo();
