<script lang="ts">
  import AutomationEditor, { fromJson, toJson, ruleProblems, type UiRule } from './AutomationEditor.svelte';
  import { onMount } from 'svelte';
  import { api, ApiError } from '../lib/api';
  import { app } from '../lib/state.svelte';
  import { plugins } from '../lib/plugins.svelte';
  import { awaitApproval } from '../lib/approvals.svelte';
  import { bridge, isWindowsClient } from '../lib/platform';

  const ALL = ['Read', 'CreateEdit', 'MoveArchive', 'TrashRestore', 'PermanentDelete', 'RunCommands', 'OutsideFiles', 'Network'] as const;
  const LABELS: Record<string, string> = {
    Read: 'Read content', CreateEdit: 'Create and edit', MoveArchive: 'Move and archive', TrashRestore: 'Trash and restore',
    PermanentDelete: 'Permanently delete (always asks)', RunCommands: 'Run commands (always asks)', OutsideFiles: 'Files outside the vault', Network: 'Network requests'
  };

  let grants = $state<{ agents: Record<string, string[]>; plugins: Record<string, string[]>; automations: Record<string, string[]> }>({ agents: {}, plugins: {}, automations: {} });
  let safeMode = $state(false);
  let newAgent = $state('');
  let automationsText = $state('');
  let automationError = $state('');
  let recent = $state<{ rule: string; title: string; at: string; outcome: string }[]>([]);

  async function load() {
    const p = await api.get<{ grants: typeof grants; safeMode: boolean }>('permissions');
    grants = p.grants; safeMode = p.safeMode;
    await plugins.load();
    const a = await api.get<any>('config/automations.json').catch(() => null);
    automationsText = JSON.stringify(a ?? { automations: [] }, null, 2);
    rules = fromJson(Array.isArray(a) ? a : a?.automations ?? []);
    recent = await api.get('automations/recent');
  }
  onMount(load);

  /** Grant changes are approved in the Windows client, so a web page can't widen anything by itself. */
  async function setGrant(kind: 'Agent' | 'Plugin' | 'Automation', name: string, permissions: string[]) {
    const body = { kind, name, permissions };
    try {
      const r = await api.put<{ grants: typeof grants }>('permissions', body);
      grants = r.grants;
    } catch (err) {
      if (err instanceof ApiError && err.approval) {
        const approval = await awaitApproval(err.approval);
        if (!approval) return;
        const r = await api.put<{ grants: typeof grants }>('permissions', { ...body, approval });
        grants = r.grants;
        app.toast(`Permissions for ${name} updated.`, 'ok');
      } else app.error(err);
    }
  }

  function toggle(kind: 'Agent' | 'Plugin' | 'Automation', name: string, current: string[], perm: string) {
    const next = current.includes(perm) ? current.filter((p) => p !== perm) : [...current, perm];
    setGrant(kind, name, next);
  }

  async function setSafe(on: boolean) {
    await api.post('system/safe-mode', { on });
    safeMode = on;
    app.toast(on ? 'Safe mode on: plugins are off. Reload to unload any running plugin.' : 'Safe mode off. Reload to start enabled plugins.', 'info', { label: 'Reload', run: () => location.reload() });
  }

  async function enable(name: string, enabled: boolean) {
    await api.post('plugins/enabled', { name, enabled });
    await plugins.load();
    if (enabled && !safeMode) plugins.mount(name); else plugins.unmount(name);
  }

  /** The form is the usual way; JSON stays available for anything the form doesn't cover. */
  let automationMode = $state<'form' | 'json'>('form');
  let rules = $state<UiRule[]>([]);

  function switchMode(to: 'form' | 'json') {
    if (to === automationMode) return;
    if (to === 'json') automationsText = JSON.stringify({ automations: toJson(rules) }, null, 2);
    else {
      try {
        const parsed = JSON.parse(automationsText);
        rules = fromJson(Array.isArray(parsed) ? parsed : parsed.automations ?? []);
      } catch (e) { automationError = `The JSON isn't valid yet: ${(e as Error).message}`; return; }
    }
    automationError = '';
    automationMode = to;
  }

  async function saveAutomations() {
    if (automationMode === 'form') {
      const problems = ruleProblems(rules);
      if (problems.length) { automationError = problems.join(' '); return; }
      automationsText = JSON.stringify({ automations: toJson(rules) }, null, 2);
    }
    try {
      const parsed = JSON.parse(automationsText);
      const list = Array.isArray(parsed) ? parsed : parsed.automations;
      if (!Array.isArray(list)) throw new Error('Expected { "automations": [ … ] }');
      for (const r of list) if (!r.name || !r.when?.property) throw new Error('Each automation needs a name and when.property');
      await api.put('config/automations.json', Array.isArray(parsed) ? { automations: parsed } : parsed);
      automationError = '';
      app.toast('Automations saved.', 'ok');
    } catch (e) { automationError = (e as Error).message; }
  }

  const example = `{
  "automations": [
    {
      "name": "Stamp finished projects",
      "when": { "tag": "project", "property": "status", "to": "Done" },
      "then": [
        { "set": { "finished_on": "{{today}}" } },
        { "createTask": { "title": "Write a retro for {{title}}", "due": "{{today+7}}" } },
        { "notify": "{{title}} is finished" }
      ]
    }
  ]
}`;
</script>

<h2>Plugins & permissions</h2>

<section>
  <h3>Safe mode</h3>
  <label class="check"><input type="checkbox" checked={safeMode} onchange={(e) => setSafe((e.target as HTMLInputElement).checked)} /> Turn off all plugins</label>
  <p class="faint">Use this if something misbehaves. Safe mode stays on until you turn it off.</p>
  {#if isWindowsClient}<button class="btn" onclick={() => bridge.send({ type: 'selfcheck' })}>Run self-check</button>{/if}
</section>

<section>
  <h3>Plugins</h3>
  <p class="faint">Plugins live in the vault's plugins folder. They run sandboxed and can only do what you grant here.</p>
  {#each plugins.list as p (p.name)}
    <div class="card">
      <div class="head">
        <strong>{p.name}</strong> <span class="faint">{p.version}</span>
        <label class="check right"><input type="checkbox" checked={p.enabled} disabled={!!p.error} onchange={(e) => enable(p.name, (e.target as HTMLInputElement).checked)} /> Enabled</label>
      </div>
      {#if p.description}<p>{p.description}</p>{/if}
      {#if p.error}<p class="warn">{p.error}</p>{/if}
      {#if p.permissions.length}<p class="faint">Asks for: {p.permissions.join(', ')}</p>{/if}
      <div class="perms">
        {#each ALL as perm (perm)}
          <label class="check"><input type="checkbox" checked={(grants.plugins[p.name] ?? []).includes(perm)} onchange={() => toggle('Plugin', p.name, grants.plugins[p.name] ?? [], perm)} /> {LABELS[perm]}</label>
        {/each}
      </div>
    </div>
  {:else}
    <p class="muted">No plugins installed.</p>
  {/each}
</section>

<section>
  <h3>Agents</h3>
  <p class="faint">AI agents connect through <code>quantos-cli mcp --agent &lt;name&gt;</code>. By default an agent can only read.</p>
  {#each Object.entries(grants.agents) as [name, perms] (name)}
    <div class="card">
      <strong>{name}</strong>
      <div class="perms">
        {#each ALL as perm (perm)}
          <label class="check"><input type="checkbox" checked={perms.includes(perm)} onchange={() => toggle('Agent', name, perms, perm)} /> {LABELS[perm]}</label>
        {/each}
      </div>
    </div>
  {/each}
  <form class="row" onsubmit={(e) => { e.preventDefault(); if (newAgent.trim()) setGrant('Agent', newAgent.trim(), ['Read']); newAgent = ''; }}>
    <input class="input" bind:value={newAgent} placeholder="Agent name, e.g. claude" /><button class="btn" type="submit">Add agent</button>
  </form>
</section>

<section>
  <h3>Automations</h3>
  <p class="faint">Run actions when a property changes. Saved in the vault's automations.json. Automations can edit content; give one more (like archiving) under Grants below.</p>
  {#if automationMode === 'form'}
    <AutomationEditor bind:rules />
  {:else}
    <textarea bind:value={automationsText} rows="14" spellcheck="false" aria-label="Automations as JSON"></textarea>
  {/if}
  {#if automationError}<p class="warn">{automationError}</p>{/if}
  <div class="row">
    <button class="btn primary" onclick={saveAutomations}>Save automations</button>
    {#if automationMode === 'form'}
      {#if !rules.length}<button class="btn ghost" onclick={() => (rules = fromJson(JSON.parse(example).automations))}>Start from an example</button>{/if}
      <button class="btn ghost" onclick={() => switchMode('json')}>Edit as JSON</button>
    {:else}
      <button class="btn ghost" onclick={() => (automationsText = example)}>Show an example</button>
      <button class="btn ghost" onclick={() => switchMode('form')}>Back to the form</button>
    {/if}
  </div>
  {#each Object.entries(grants.automations) as [name, perms] (name)}
    <div class="card"><strong>{name}</strong>
      <div class="perms">{#each ALL.slice(0, 4) as perm (perm)}<label class="check"><input type="checkbox" checked={perms.includes(perm)} onchange={() => toggle('Automation', name, perms, perm)} /> {LABELS[perm]}</label>{/each}</div></div>
  {/each}
  {#if recent.length}
    <h3>Recent runs</h3>
    {#each recent.slice(0, 12) as r, i (i)}<p class="run"><span class="faint">{new Date(r.at).toLocaleString()}</span> {r.rule} on “{r.title}”: {r.outcome}</p>{/each}
  {/if}
</section>

<style>
  h2 { font: 700 24px/1.2 var(--font-display); margin-bottom: 8px; }
  h3 { font: 700 16px/1.3 var(--font-display); margin: 22px 0 6px; }
  section { margin-bottom: 18px; }
  .card { border: 1px solid var(--line); border-radius: 9px; padding: 10px 12px; margin: 8px 0; background: var(--bg-raised); }
  .head { display: flex; gap: 8px; align-items: center; }
  .right { margin-left: auto; }
  .perms { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 2px 10px; margin-top: 6px; }
  .check { display: flex; gap: 6px; align-items: center; font-size: 13.5px; color: var(--text-soft); }
  .warn { color: var(--warn); font-size: 13px; }
  .row { display: flex; gap: 8px; margin-top: 8px; }
  .row .input { max-width: 260px; }
  textarea { width: 100%; font: 12.5px var(--font-mono); background: var(--bg-input); border: 1px solid var(--line-strong); border-radius: 8px; padding: 10px; color: var(--text); }
  code { color: var(--accent); }
  .run { font-size: 13px; margin: 2px 0; }
</style>
