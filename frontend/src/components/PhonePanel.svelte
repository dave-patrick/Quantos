<script lang="ts">
  import { app } from '../lib/state.svelte';
  import FileTree from './FileTree.svelte';
  import InspectorPanel from './InspectorPanel.svelte';
  import LinksPanel from './LinksPanel.svelte';
  import NotificationsPanel from './NotificationsPanel.svelte';
  import LocalGraph from './LocalGraph.svelte';
  import Scratchpad from './Scratchpad.svelte';
  import TasksPanel from './TasksPanel.svelte';
  import QuickAdd from './QuickAdd.svelte';
  import MyDayView from './views/MyDayView.svelte';
  let { today }: { today: string } = $props();
</script>

<section class="phone-panel" aria-label="Note details and files">
  <label class="panel-picker">Show
    <select class="input" bind:value={app.rightTab}>
      <option value="files">Files</option><option value="inspector">Properties</option><option value="links">Links</option>
      <option value="graph">Local graph</option><option value="scratch">Scratchpad</option><option value="notifications">Notifications</option>
      <option value="tasks">Tasks</option><option value="quickadd">Quick add</option><option value="myday">My Day</option>
    </select>
  </label>
  <div class="panel-content">
    {#if app.rightTab === 'files'}<FileTree />
    {:else if app.rightTab === 'inspector'}<InspectorPanel />
    {:else if app.rightTab === 'links'}<LinksPanel />
    {:else if app.rightTab === 'graph'}<LocalGraph />
    {:else if app.rightTab === 'scratch'}<Scratchpad docked />
    {:else if app.rightTab === 'tasks'}<TasksPanel />
    {:else if app.rightTab === 'quickadd'}<div class="quick"><QuickAdd /></div>
    {:else if app.rightTab === 'myday'}<MyDayView {today} docked />
    {:else}<NotificationsPanel />{/if}
  </div>
</section>

<style>
  .phone-panel { display: flex; flex-direction: column; min-height: 0; flex: 1; }
  .panel-picker { display: flex; align-items: center; gap: 16px; padding: 12px 16px; color: var(--text-muted); border-bottom: 1px solid var(--line); }
  select { flex: 1; min-width: 0; }
  .panel-content { flex: 1; min-height: 0; display: flex; flex-direction: column; }
  .quick { padding: 16px; }
</style>
