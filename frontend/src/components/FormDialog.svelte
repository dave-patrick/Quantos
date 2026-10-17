<script lang="ts">
  import { api } from '../lib/api';
  import { app } from '../lib/state.svelte';
  import { openNode } from '../lib/links.svelte';
  import type { Supertag, FieldDef } from '../lib/supertags.svelte';
  import type { NodeDto } from '../lib/types';
  import { modal } from '../lib/modal';

  /** Forms validate strictly: nothing is created until every required field is valid (BUILD-PLAN 8C.4). */
  let { tag, onclose }: { tag: Supertag; onclose: () => void } = $props();

  let title = $state('');
  let values = $state<Record<string, unknown>>(Object.fromEntries(tag.allFields.map((f) => [f.key, f.default ?? (f.type === 'checkbox' ? false : '')])));
  let tried = $state(false);
  let busy = $state(false);

  function problem(f: FieldDef): string | null {
    const v = values[f.key];
    const empty = v === '' || v === null || v === undefined || (Array.isArray(v) && !v.length);
    if (f.required && empty) return 'Required';
    if (empty) return null;
    if (f.type === 'number' && Number.isNaN(Number(v))) return 'Enter a number';
    if (f.type === 'url' && !/^https?:\/\/\S+$/.test(String(v))) return 'Enter a web address starting with http';
    if (f.type === 'email' && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(String(v))) return 'Enter an email address';
    if ((f.type === 'select' || f.type === 'status') && f.options?.length && !f.options.includes(String(v))) return 'Choose an option';
    return null;
  }

  const errors = $derived(tag.allFields.map((f) => [f.key, problem(f)] as const).filter(([, p]) => p));
  const valid = $derived(title.trim().length > 0 && errors.length === 0);

  async function submit() {
    tried = true;
    if (!valid) return;
    busy = true;
    try {
      const properties = Object.fromEntries(Object.entries(values)
        .filter(([, v]) => v !== '' && v !== null && v !== undefined)
        .map(([k, v]) => {
          const f = tag.allFields.find((x) => x.key === k);
          if (f?.type === 'number') return [k, Number(v)];
          if (f?.type === 'multiselect' || f?.type === 'nodes') return [k, String(v).split(',').map((s) => s.trim()).filter(Boolean)];
          return [k, v];
        }));
      const node = await api.post<NodeDto>('nodes', { title: title.trim(), workspace: app.workspace, tags: [tag.name], properties });
      onclose();
      openNode(node.id, 'new', node.title);
    } catch (e) { app.error(e); } finally { busy = false; }
  }
</script>

<div class="scrim">
  <form class="dialog" onsubmit={(e) => { e.preventDefault(); submit(); }}>
    <h2>New #{tag.name}</h2>
    <label class:bad={tried && !title.trim()}>Title <!-- svelte-ignore a11y_autofocus --><input class="input" bind:value={title} autofocus /></label>
    {#each tag.allFields as f (f.key)}
      {@const p = tried ? problem(f) : null}
      <label class:bad={!!p}>{f.name}{f.required ? ' *' : ''}
        {#if f.type === 'checkbox'}<input type="checkbox" bind:checked={values[f.key] as boolean} />
        {:else if (f.type === 'select' || f.type === 'status') && f.options?.length}
          <select class="input" bind:value={values[f.key]}><option value=""></option>{#each f.options as o (o)}<option value={o}>{o}</option>{/each}</select>
        {:else}
          <input class="input" type={f.type === 'date' ? 'date' : f.type === 'datetime' ? 'datetime-local' : f.type === 'number' ? 'number' : f.type === 'email' ? 'email' : 'text'} bind:value={values[f.key]} />
        {/if}
        {#if p}<span class="problem">{p}</span>{/if}
      </label>
    {/each}
    <div class="actions">
      <button type="button" class="btn" onclick={onclose}>Cancel</button>
      <button type="submit" class="btn primary" disabled={busy || (tried && !valid)}>Create</button>
    </div>
  </form>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 80; background: rgb(4 8 14 / .6); display: grid; place-items: center; padding: 16px; }
  .dialog { width: min(520px, 100%); max-height: 86vh; overflow: auto; background: var(--bg-raised); border: 1px solid var(--line-strong); border-radius: 12px; padding: 18px 20px; display: grid; gap: 10px; box-shadow: var(--shadow); }
  h2 { font: 700 19px/1.3 var(--font-display); }
  label { display: grid; gap: 4px; font-size: 13px; color: var(--text-muted); }
  label.bad .input { border-color: var(--danger); }
  .problem { color: var(--danger); font-size: 12px; }
  .actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 6px; }
</style>
