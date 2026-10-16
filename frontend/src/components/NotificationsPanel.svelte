<script lang="ts">
  import { onMount } from 'svelte';
  import { notifications } from '../lib/notifications.svelte';
  import { openNode } from '../lib/links.svelte';
  import { relativeTime } from '../lib/dates';
  import { isWindowsClient } from '../lib/platform';
  let permission = $state(typeof Notification === 'undefined' ? 'unsupported' : Notification.permission);
  onMount(() => notifications.load());
</script>

<div class="scroll notes">
  <div class="head"><h3 class="label">Notification history</h3>
    {#if notifications.unread}<button class="btn ghost" onclick={() => notifications.markRead()}>Mark all read</button>{/if}</div>
  {#if !isWindowsClient && permission === 'default'}
    <button class="btn" onclick={async () => (permission = await Notification.requestPermission())}>Show reminders as browser notifications</button>
  {/if}
  {#each notifications.list as n (n.id)}
    <button class="note" class:unread={!n.read} onclick={() => { notifications.markRead([n.id]); if (n.nodeId) openNode(n.nodeId, 'new'); }}>
      <strong>{n.title}</strong><span>{n.body}</span><span class="faint">{relativeTime(n.at)}</span>
    </button>
  {:else}
    <p class="faint">No notifications yet. Reminders appear here.</p>
  {/each}
</div>

<style>
  .notes { flex: 1; padding: 10px; }
  .head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
  .note { display: grid; gap: 2px; width: 100%; text-align: left; border: 0; background: none; padding: 8px; border-radius: 7px; color: var(--text-soft); font-size: 13px; border-left: 2px solid transparent; }
  .note:hover { background: var(--bg-hover); }
  .note.unread { border-left-color: var(--accent); background: var(--accent-wash); }
  strong { color: var(--text); }
</style>
