import { api } from './api';
import { onChange } from './events';
import { isWindowsClient, isAndroidClient, bridge } from './platform';
import type { Approval } from './types';

/**
 * Shell execution and permanent deletion need approval in the Windows client. A browser request
 * alone can never trigger them, so the browser shows a waiting state until the client decides.
 */
class Approvals {
  waiting = $state<Approval | null>(null);
  private resolver: ((id: string | null) => void) | null = null;

  constructor() {
    onChange((e) => {
      if (e.kind === 'Approval' && this.waiting && e.data?.id === this.waiting.id) this.poll();
    });
  }

  wait(approval: Approval): Promise<string | null> {
    this.waiting = approval;
    if (isAndroidClient) bridge.send({ type: 'approve', id: approval.id });
    return new Promise((resolve) => {
      this.resolver = resolve;
      const timer = setInterval(async () => {
        if (!this.waiting) { clearInterval(timer); return; }
        await this.poll();
      }, 1200);
    });
  }

  async poll() {
    if (!this.waiting) return;
    const current = await api.get<Approval>(`approvals/${this.waiting.id}`).catch(() => null);
    if (!current || current.state === 'Pending') return;
    const id = current.state === 'Approved' ? current.id : null;
    this.finish(id);
  }

  cancel() { this.finish(null); }

  private finish(id: string | null) {
    this.waiting = null;
    this.resolver?.(id);
    this.resolver = null;
  }

  get inClient() { return isWindowsClient || isAndroidClient; }
}

export const approvals = new Approvals();
export const awaitApproval = (a: Approval) => approvals.wait(a);
