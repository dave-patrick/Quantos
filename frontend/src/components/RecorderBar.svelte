<script lang="ts">
  import { recorder } from '../lib/recorder.svelte';
  import { clock } from '../lib/focus.svelte';

  /** The floating bar shown while recording: time, input level, stop and discard. */
</script>

{#if recorder.state !== 'idle'}
  <div class="rec" role="region" aria-label="Audio recording">
    <span class="dot" class:live={recorder.state === 'recording'}></span>
    <span class="time" aria-live="off">{recorder.state === 'recording' ? clock(recorder.elapsed) : recorder.state === 'saving' ? 'Saving…' : 'Starting…'}</span>
    <span class="meter" aria-hidden="true"><span style={`width:${Math.round(recorder.level * 100)}%`}></span></span>
    <button class="btn primary" disabled={recorder.state !== 'recording'} onclick={() => recorder.stop()}>Stop and save</button>
    <button class="btn ghost" disabled={recorder.state !== 'recording'} onclick={() => recorder.cancel()}>Discard</button>
  </div>
{/if}

<style>
  .rec { position: fixed; left: 50%; bottom: 40px; transform: translateX(-50%); z-index: 900; display: flex; align-items: center; gap: 10px; padding: 8px 10px 8px 14px; background: var(--bg-panel); border: 1px solid var(--line-strong); border-radius: 12px; box-shadow: var(--shadow); }
  .dot { width: 10px; height: 10px; border-radius: 50%; background: var(--text-faint); }
  .dot.live { background: var(--danger); animation: pulse 1.2s infinite alternate; }
  @keyframes pulse { to { opacity: .35; } }
  .time { font: 600 14px var(--font-mono); min-width: 52px; }
  .meter { width: 70px; height: 6px; background: var(--line); border-radius: 3px; overflow: hidden; }
  .meter span { display: block; height: 100%; background: var(--ok); transition: width .08s; }
</style>
