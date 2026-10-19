import { api } from './api';
import { activeEditor } from './activeEditor';
import { uploadAttachment } from './editor/attachments';
import { app } from './state.svelte';

/**
 * Audio recording (Release 1.1). The recording is stored as an attachment in the vault (Opus in WebM)
 * and its link goes into the note being edited, or today's daily note when no note is open.
 */
const MAX_MS = 2 * 60 * 60 * 1000;

class Recorder {
  state = $state<'idle' | 'starting' | 'recording' | 'saving'>('idle');
  started = $state(0);
  now = $state(0);
  level = $state(0);
  private media: MediaRecorder | null = null;
  private stream: MediaStream | null = null;
  private chunks: Blob[] = [];
  private keep = true;
  private timer: ReturnType<typeof setInterval> | undefined;
  private audio: AudioContext | null = null;
  private target: { view: typeof activeEditor.view; pos: number | null } = { view: null, pos: null };

  get elapsed() { return this.state === 'recording' ? this.now - this.started : 0; }

  async start() {
    if (this.state !== 'idle') return;
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') { app.toast('This browser can’t record audio.', 'error'); return; }
    this.state = 'starting';
    // Remember where the link goes now, before focus moves to the recorder.
    this.target = { view: activeEditor.view, pos: activeEditor.view?.state.selection.main.head ?? activeEditor.lastPos };
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
    } catch (e) {
      this.state = 'idle';
      const name = (e as DOMException).name;
      app.toast(name === 'NotAllowedError' ? 'Quantos isn’t allowed to use the microphone. Allow it in the browser (or Windows privacy settings) and try again.'
        : name === 'NotFoundError' ? 'No microphone was found.' : 'The microphone couldn’t be started: ' + (e as Error).message, 'error');
      return;
    }
    const type = ['audio/webm;codecs=opus', 'audio/ogg;codecs=opus', 'audio/webm'].find((t) => MediaRecorder.isTypeSupported(t));
    this.media = new MediaRecorder(this.stream, type ? { mimeType: type, audioBitsPerSecond: 64_000 } : undefined);
    this.chunks = [];
    this.keep = true;
    this.media.ondataavailable = (e) => { if (e.data.size) this.chunks.push(e.data); };
    this.media.onstop = () => this.finish();
    this.media.start(1000);
    this.started = this.now = Date.now();
    this.state = 'recording';
    this.meter();
    this.timer = setInterval(() => {
      this.now = Date.now();
      if (this.elapsed > MAX_MS) { app.toast('Recordings stop after two hours.', 'info'); this.stop(); }
    }, 250);
  }

  stop() { if (this.media?.state === 'recording') { this.keep = true; this.media.stop(); } }
  cancel() { if (this.media?.state === 'recording') { this.keep = false; this.media.stop(); } }

  /** A simple input level, so it's obvious the microphone is hearing something. */
  private meter() {
    try {
      this.audio = new AudioContext();
      const src = this.audio.createMediaStreamSource(this.stream!);
      const an = this.audio.createAnalyser();
      an.fftSize = 512;
      src.connect(an);
      const data = new Uint8Array(an.fftSize);
      const loop = () => {
        if (this.state !== 'recording') return;
        an.getByteTimeDomainData(data);
        let peak = 0;
        for (const v of data) peak = Math.max(peak, Math.abs(v - 128));
        this.level = Math.min(1, peak / 64);
        requestAnimationFrame(loop);
      };
      loop();
    } catch { /* no meter */ }
  }

  private async finish() {
    clearInterval(this.timer);
    this.stream?.getTracks().forEach((t) => t.stop());
    this.audio?.close().catch(() => {});
    const ms = Date.now() - this.started;
    const type = this.media?.mimeType || 'audio/webm';
    this.media = null;
    if (!this.keep || !this.chunks.length) { this.state = 'idle'; return; }
    this.state = 'saving';
    try {
      const at = new Date(this.started);
      const pad = (n: number) => String(n).padStart(2, '0');
      const name = `Recording ${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())} ${pad(at.getHours())}-${pad(at.getMinutes())}.${type.includes('ogg') ? 'ogg' : 'webm'}`;
      const md = await uploadAttachment(new File(this.chunks, name, { type }));
      const length = `${Math.floor(ms / 60000)}:${pad(Math.floor((ms % 60000) / 1000))}`;
      const line = `${md} (${length})`;
      const view = this.target.view;
      if (view && view.dom.isConnected) {
        const pos = Math.min(this.target.pos ?? view.state.doc.length, view.state.doc.length);
        const lineEnd = view.state.doc.lineAt(pos).to;
        const before = view.state.doc.lineAt(pos).text.trim() ? '\n' : '';
        view.dispatch({ changes: { from: lineEnd, insert: before + line + '\n' }, selection: { anchor: lineEnd + before.length + line.length + 1 } });
        app.toast(`Recording saved (${length}).`, 'ok');
      } else {
        await api.post('capture', { text: line, workspace: app.workspace, target: 'daily' });
        app.toast(`Recording saved to today's note (${length}).`, 'ok');
      }
    } catch (e) { app.error(e); }
    finally { this.chunks = []; this.state = 'idle'; }
  }
}

export const recorder = new Recorder();
