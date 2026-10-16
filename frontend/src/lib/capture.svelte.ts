import { api } from './api';
import { app } from './state.svelte';

export type CaptureTarget = 'daily' | 'inbox' | 'task';

class Capture {
  open = $state(false);
  text = $state('');
  target = $state<CaptureTarget>('daily');
  workspace = $state('');

  show(text = '') {
    this.text = text;
    this.workspace = app.workspace;
    this.open = true;
  }

  async submit() {
    const text = this.text.trim();
    if (!text) return;
    await api.post('capture', { text, workspace: this.workspace || app.workspace, target: this.target });
    app.toast(this.target === 'task' ? 'Task captured.' : this.target === 'inbox' ? 'Captured to a new note.' : "Added to today's daily note.", 'ok');
    this.text = '';
    this.open = false;
  }
}

export const capture = new Capture();
