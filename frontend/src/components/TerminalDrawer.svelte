<script lang="ts">
  import { tick } from 'svelte';
  import { terminal } from '../lib/terminal.svelte';
  import Icon from './Icon.svelte';
  import { isAndroidClient } from '../lib/platform';

  let input = $state('');
  let history = $state<string[]>([]);
  let cursor = $state(-1);
  let out = $state<HTMLPreElement | null>(null);
  let field = $state<HTMLInputElement | null>(null);

  const session = $derived(terminal.sessions.find((s) => s.id === terminal.active) ?? null);
  $effect(() => { void session?.output; tick().then(() => { if (out) out.scrollTop = out.scrollHeight; }); });

  function onkeydown(e: KeyboardEvent) {
    if (!session) return;
    if (e.key === 'Enter') {
      e.preventDefault();
      terminal.input(session.id, input);
      if (input.trim()) history = [...history.filter((h) => h !== input), input].slice(-100);
      input = ''; cursor = -1;
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      cursor = cursor < 0 ? history.length - 1 : Math.max(0, cursor - 1);
      input = history[cursor] ?? '';
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      cursor = cursor < 0 ? -1 : cursor + 1;
      input = cursor >= 0 && cursor < history.length ? history[cursor] : '';
      if (cursor >= history.length) cursor = -1;
    } else if (e.key === 'c' && e.ctrlKey && !window.getSelection()?.toString()) {
      e.preventDefault();
      terminal.stop(session.id);
    } else if (e.key === 'l' && e.ctrlKey) {
      e.preventDefault();
      session.output = '';
    }
  }

  let dragging = false;
  function startResize(e: PointerEvent) {
    dragging = true;
    const start = e.clientY, h = terminal.height;
    const move = (ev: PointerEvent) => { if (dragging) terminal.height = Math.max(140, Math.min(innerHeight - 200, h + (start - ev.clientY))); };
    const up = () => { dragging = false; removeEventListener('pointermove', move); removeEventListener('pointerup', up); };
    addEventListener('pointermove', move);
    addEventListener('pointerup', up);
  }
</script>

<section class="drawer" style="height:{terminal.height}px" aria-label={isAndroidClient ? 'Android shell' : 'PowerShell terminal'}>
  <div class="grip" role="separator" aria-orientation="horizontal" onpointerdown={startResize}></div>
  <header>
    <Icon name="terminal" size={14} />
    {#each terminal.sessions as s, i (s.id)}
      <button class="tab" class:on={s.id === terminal.active} onclick={() => (terminal.active = s.id)}>{isAndroidClient ? 'Shell' : 'PowerShell'} {i + 1}{s.running ? ' ●' : ''}</button>
    {/each}
    <button class="icon-btn" title="New terminal" onclick={() => terminal.newSession()}><Icon name="plus" size={13} /></button>
    <span class="spacer"></span>
    {#if session}<button class="icon-btn" title="Close this terminal" onclick={() => terminal.close(session.id)}><Icon name="trash" size={13} /></button>{/if}
    <button class="icon-btn" title="Hide terminal" onclick={() => (terminal.open = false)}><Icon name="chevron-down" size={14} /></button>
  </header>
  {#if session}
    <!-- Clicking the output focuses the command line; the input itself stays keyboard-reachable. -->
    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_noninteractive_element_interactions -->
    <pre bind:this={out} class="out" onclick={() => field?.focus()} role="log">{session.output}</pre>
    <div class="line">
      <span class="prompt">{isAndroidClient ? '$' : `PS ${session.cwd}>`}</span>
      <!-- svelte-ignore a11y_autofocus -->
      <input bind:this={field} bind:value={input} {onkeydown} spellcheck="false" autocomplete="off" autofocus aria-label="Command" disabled={session.running} />
    </div>
  {:else}
    <p class="empty">{terminal.denied ? 'Opening the terminal was cancelled.' : 'Approve the terminal in the confirmation window to start.'}</p>
  {/if}
</section>

<style>
  .drawer { position: relative; flex: none; display: flex; flex-direction: column; border-top: 1px solid var(--line-strong); background: #070c13; }
  .grip { position: absolute; top: -3px; left: 0; right: 0; height: 6px; cursor: ns-resize; }
  header { display: flex; align-items: center; gap: 4px; padding: 3px 8px; border-bottom: 1px solid var(--line); color: var(--text-faint); }
  .tab { border: 0; background: none; color: var(--text-muted); font: 12px var(--font-mono); padding: 3px 8px; border-radius: 4px; }
  .tab.on { background: var(--bg-active); color: var(--text); }
  .spacer { flex: 1; }
  .out { flex: 1; margin: 0; padding: 8px 12px; overflow: auto; font: 12.5px/1.45 var(--font-mono); color: #d6e2f1; white-space: pre-wrap; overflow-wrap: anywhere; }
  .line { display: flex; align-items: center; gap: 6px; padding: 4px 12px 8px; font: 12.5px var(--font-mono); }
  .prompt { color: var(--accent); white-space: nowrap; max-width: 45%; overflow: hidden; text-overflow: ellipsis; }
  .line input { flex: 1; border: 0; background: transparent; color: var(--text); font: inherit; }
  .line input:focus { outline: none; box-shadow: none; }
  .empty { padding: 16px; color: var(--text-faint); font-size: 13px; margin: 0; }
</style>
