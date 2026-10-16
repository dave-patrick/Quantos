<script lang="ts">
  import type { Doc } from '../lib/docs.svelte';
  import { supertags, type FieldDef } from '../lib/supertags.svelte';
  import { app } from '../lib/state.svelte';
  import { openNode, searchTitles } from '../lib/links.svelte';
  import Icon from './Icon.svelte';

  let { doc, compact = false }: { doc: Doc; compact?: boolean } = $props();

  const hiddenKeys = new Set(['status', 'priority', 'due', 'planned', 'effort', 'assignees', 'project', 'waiting_on', 'parent', 'depends_on',
    'recurrence', 'series', 'occurrence', 'reminders', 'completed', 'source', 'time_block', 'tz', 'date', 'stage']);

  const node = $derived(doc.node);
  const tagDefs = $derived((node?.tags ?? []).map((t) => supertags.byName(t)).filter((t) => !!t));
  const fields = $derived.by(() => {
    const seen = new Set<string>();
    const out: FieldDef[] = [];
    for (const tag of tagDefs) for (const f of tag!.allFields) if (!seen.has(f.key)) { seen.add(f.key); out.push(f); }
    return out;
  });
  const freeKeys = $derived(Object.keys(node?.properties ?? {}).filter((k) => !fields.some((f) => f.key === k) && !(node?.type === 'task' && hiddenKeys.has(k)) && !(node?.type === 'daily' && k === 'date')));

  let adding = $state(false);
  let newKey = $state('');
  let newValue = $state('');
  let tagInput = $state('');
  let showTagInput = $state(false);
  let tagEl = $state<HTMLInputElement | null>(null);
  // autofocus only applies on page load; focus the supertag box when it appears.
  $effect(() => { if (showTagInput) tagEl?.focus(); });

  function display(value: unknown): string {
    if (value === null || value === undefined) return '';
    if (Array.isArray(value)) return value.map((v) => display(v)).join(', ');
    if (typeof value === 'object') return JSON.stringify(value);
    return String(value);
  }

  function parse(text: string, previous: unknown): unknown {
    const t = text.trim();
    if (Array.isArray(previous)) return t ? t.split(',').map((s) => s.trim()).filter(Boolean) : [];
    if (typeof previous === 'number' && t !== '' && !Number.isNaN(Number(t))) return Number(t);
    if (typeof previous === 'boolean') return t === 'true';
    return t;
  }

  /** Forms validate strictly; freeform properties warn but never block (BUILD-PLAN 8C.4-5). */
  function validate(field: FieldDef, value: unknown): string | null {
    const empty = value === null || value === undefined || value === '' || (Array.isArray(value) && value.length === 0);
    if (field.required && empty) return 'Required';
    if (empty) return null;
    if (field.type === 'number' && Number.isNaN(Number(value))) return 'Expected a number';
    if ((field.type === 'date') && !/^\d{4}-\d{2}-\d{2}$/.test(String(value))) return 'Expected a date (YYYY-MM-DD)';
    if (field.type === 'select' && field.options?.length && !field.options.includes(String(value))) return `Expected one of: ${field.options.join(', ')}`;
    if (field.type === 'url' && !/^https?:\/\//.test(String(value))) return 'Expected a web address';
    if (field.type === 'email' && !/^[^@\s]+@[^@\s]+$/.test(String(value))) return 'Expected an email address';
    return null;
  }

  async function setValue(key: string, value: unknown) {
    await doc.setProperties({ [key]: value });
  }

  async function removeKey(key: string) {
    await doc.setProperties({}, [key]);
  }

  async function addProperty() {
    const key = newKey.trim().toLowerCase().replace(/\s+/g, '_').replace(/[^\w\-.]/g, '');
    if (!key) return;
    if (['id', 'title', 'type', 'workspace', 'created', 'modified', 'schema', 'archived', 'tags'].includes(key)) { app.toast(`“${key}” is managed by Quantos.`, 'warn'); return; }
    await setValue(key, newValue.trim());
    adding = false; newKey = ''; newValue = '';
  }

  async function addTag() {
    const tag = tagInput.trim().replace(/^#/, '');
    if (!tag || !node) return;
    if (!node.tags.includes(tag)) await doc.setProperties({ tags: [...node.tags, tag] });
    tagInput = ''; showTagInput = false;
  }

  async function removeTag(tag: string) {
    if (!node) return;
    await doc.setProperties({ tags: node.tags.filter((t) => t !== tag) });
  }

  const listOf = (value: unknown): string[] => Array.isArray(value) ? value.map(String) : value === null || value === undefined || value === '' ? [] : [String(value)];
  const linkCandidates = (q: string) => searchTitles(q, 8);
  const linked = (value: unknown) => (typeof value === 'string' ? /^\[\[([0-9A-HJKMNP-TV-Z]{26})\|([^\]]+)\]\]$/.exec(value) : null);
</script>

{#if node}
  <section class="props" class:compact aria-label="Properties">
    <div class="tags">
      {#each node.tags as tag (tag)}
        {@const def = supertags.byName(tag)}
        <span class="tag" style="--tag:{def?.color ?? 'var(--accent)'}">#{tag}
          <button aria-label="Remove tag {tag}" onclick={() => removeTag(tag)}><Icon name="x" size={10} /></button></span>
      {/each}
      {#if showTagInput}
        <form onsubmit={(e) => { e.preventDefault(); addTag(); }}>
          <!-- svelte-ignore a11y_autofocus -->
          <input class="tag-input" list="tag-options" bind:this={tagEl} bind:value={tagInput} placeholder="supertag" autofocus onblur={() => { if (!tagInput) showTagInput = false; }} />
          <datalist id="tag-options">{#each supertags.list as t (t.id)}<option value={t.name}></option>{/each}</datalist>
        </form>
      {:else}
        <button class="add-tag" onclick={() => (showTagInput = true)}><Icon name="hash" size={12} /> Add supertag</button>
      {/if}
    </div>

    <div class="aliases">
      <span class="faint">Also known as</span>
      <input class="cell" value={node.aliases.join(', ')} placeholder="other names for links and search"
        onchange={(e) => doc.setProperties({ aliases: (e.target as HTMLInputElement).value.split(',').map((s) => s.trim()).filter(Boolean) })} />
    </div>

    {#if fields.length || freeKeys.length || adding}
      <dl class="grid">
        {#each fields as field (field.key)}
          {@const value = node.properties[field.key]}
          {@const problem = validate(field, value)}
          <dt title={field.description ?? field.inherited ? `From #${field.inherited}` : ''}>{field.name}{field.required ? ' *' : ''}</dt>
          <dd class:invalid={!!problem}>
            {#if field.type === 'checkbox'}
              <input type="checkbox" checked={value === true || value === 'true'} onchange={(e) => setValue(field.key, (e.target as HTMLInputElement).checked)} />
            {:else if field.type === 'select' || field.type === 'status'}
              <select class="cell" value={display(value)} onchange={(e) => setValue(field.key, (e.target as HTMLSelectElement).value)}>
                <option value=""></option>
                {#each field.options ?? [] as o (o)}<option value={o}>{o}</option>{/each}
              </select>
            {:else if field.type === 'date'}
              <input class="cell" type="date" value={display(value)} onchange={(e) => setValue(field.key, (e.target as HTMLInputElement).value)} />
            {:else if field.type === 'number'}
              <input class="cell" type="number" value={display(value)} onchange={(e) => setValue(field.key, Number((e.target as HTMLInputElement).value))} />
            {:else if field.type === 'datetime'}
              <input class="cell" type="datetime-local" value={display(value).slice(0, 16)} onchange={(e) => setValue(field.key, (e.target as HTMLInputElement).value)} />
            {:else if field.type === 'nodes' || field.type === 'multiselect'}
              {@const items = listOf(value)}
              <div class="chips">
                {#each items as item, i (item + i)}
                  {@const m = linked(item)}
                  <span class="chip">
                    {#if m}<button class="node-link" onclick={(e) => openNode(m[1], e.shiftKey ? 'other' : 'current')}>{m[2]}</button>
                    {:else}<span class="chip-text">{item}</span>{/if}
                    <button class="chip-x" aria-label="Remove {m ? m[2] : item}" onclick={() => setValue(field.key, items.filter((_, j) => j !== i))}><Icon name="x" size={10} /></button>
                  </span>
                {/each}
                {#if field.type === 'multiselect' && field.options?.length}
                  <select class="cell add-item" value="" aria-label="Add {field.name}" onchange={(e) => {
                    const v = (e.target as HTMLSelectElement).value; if (v && !items.includes(v)) setValue(field.key, [...items, v]); (e.target as HTMLSelectElement).value = ''; }}>
                    <option value="">Add…</option>
                    {#each field.options.filter((o) => !items.includes(o)) as o (o)}<option value={o}>{o}</option>{/each}
                  </select>
                {:else}
                  <input class="cell add-item" list="items-{field.key}" placeholder={field.type === 'nodes' ? 'Link a note…' : 'Add…'} onchange={(e) => {
                    const input = e.target as HTMLInputElement; const v = input.value.trim(); if (!v) return;
                    const hit = field.type === 'nodes' ? linkCandidates(v).find((n) => n.title === v) : null;
                    setValue(field.key, [...items, hit ? `[[${hit.id}|${hit.title}]]` : v]); input.value = ''; }} />
                  {#if field.type === 'nodes'}<datalist id="items-{field.key}">{#each app.titles.slice(0, 200) as n (n.id)}<option value={n.title}></option>{/each}</datalist>{/if}
                {/if}
              </div>
            {:else if (field.type === 'node' || field.type === 'person') && linked(value)}
              {@const m = linked(value)!}
              <button class="node-link" onclick={(e) => openNode(m[1], e.shiftKey ? 'other' : 'current')}>{m[2]}</button>
              <button class="clear" aria-label="Clear" onclick={() => setValue(field.key, '')}><Icon name="x" size={11} /></button>
            {:else if field.type === 'node' || field.type === 'person'}
              <input class="cell" list="nodes-{field.key}" placeholder="Link a note…" onchange={(e) => {
                const v = (e.target as HTMLInputElement).value; const hit = linkCandidates(v).find((n) => n.title === v);
                setValue(field.key, hit ? `[[${hit.id}|${hit.title}]]` : v); }} />
              <datalist id="nodes-{field.key}">{#each app.titles.filter((t) => field.type !== 'person' || t.type === 'person').slice(0, 200) as n (n.id)}<option value={n.title}></option>{/each}</datalist>
            {:else}
              <input class="cell" value={display(value)} onchange={(e) => setValue(field.key, parse((e.target as HTMLInputElement).value, value))} />
            {/if}
            {#if problem}<span class="problem" title={problem}>{problem}</span>{/if}
          </dd>
        {/each}

        {#each freeKeys as key (key)}
          {@const value = node.properties[key]}
          <dt>{key}</dt>
          <dd>
            {#if typeof value === 'boolean'}
              <input type="checkbox" checked={value} onchange={(e) => setValue(key, (e.target as HTMLInputElement).checked)} />
            {:else if typeof value === 'object' && value !== null && !Array.isArray(value)}
              <code class="faint" title="Edit structured values in the source view">{display(value)}</code>
            {:else}
              <input class="cell" value={display(value)} onchange={(e) => setValue(key, parse((e.target as HTMLInputElement).value, value))} />
            {/if}
            <button class="clear" aria-label="Remove {key}" onclick={() => removeKey(key)}><Icon name="x" size={11} /></button>
          </dd>
        {/each}

        {#if adding}
          <dt><!-- svelte-ignore a11y_autofocus --><input class="cell key" bind:value={newKey} placeholder="property" autofocus /></dt>
          <dd><input class="cell" bind:value={newValue} placeholder="value" onkeydown={(e) => { if (e.key === 'Enter') addProperty(); if (e.key === 'Escape') adding = false; }} />
            <button class="btn" onclick={addProperty}>Add</button></dd>
        {/if}
      </dl>
    {/if}
    {#if !adding}<button class="add" onclick={() => (adding = true)}><Icon name="plus" size={12} /> Add property</button>{/if}
  </section>
{/if}

<style>
  .props { margin: 4px 0 16px; padding-bottom: 12px; border-bottom: 1px solid var(--line); }
  .tags { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; margin-bottom: 8px; }
  .tag { display: inline-flex; align-items: center; gap: 4px; padding: 2px 4px 2px 9px; border-radius: 999px; font: 600 12px/18px var(--font-mono);
    color: color-mix(in srgb, var(--tag) 70%, var(--text)); background: color-mix(in srgb, var(--tag) 14%, transparent); border: 1px solid color-mix(in srgb, var(--tag) 40%, transparent); }
  .tag button { border: 0; background: none; color: inherit; padding: 2px; border-radius: 50%; display: grid; opacity: .6; }
  .tag button:hover { opacity: 1; }
  .add-tag, .add { display: inline-flex; align-items: center; gap: 4px; border: 0; background: none; color: var(--text-faint); font-size: 12.5px; padding: 3px 6px; border-radius: 5px; }
  .add-tag:hover, .add:hover { color: var(--accent); background: var(--bg-hover); }
  .tag-input { height: 24px; border-radius: 999px; border: 1px solid var(--accent); background: var(--bg-input); padding: 0 10px; font: 12px var(--font-mono); width: 140px; }
  .aliases { display: flex; align-items: center; gap: 8px; font-size: 12.5px; margin-bottom: 6px; }
  .aliases .cell { font-size: 13px; }
  .grid { display: grid; grid-template-columns: minmax(110px, 180px) 1fr; gap: 2px 14px; margin: 0; align-items: center; }
  dt { font: 12.5px/1.3 var(--font-mono); color: var(--text-muted); padding: 5px 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  dd { margin: 0; display: flex; align-items: center; gap: 6px; min-width: 0; }
  dd.invalid .cell { border-color: color-mix(in srgb, var(--warn) 60%, transparent); }
  .cell { flex: 1; min-width: 0; height: 28px; padding: 0 8px; border-radius: 5px; border: 1px solid transparent; background: transparent; color: var(--text); font-size: 14px; }
  .cell:hover { border-color: var(--line); }
  .cell:focus { outline: none; border-color: var(--accent); background: var(--bg-input); }
  .cell.key { font-family: var(--font-mono); font-size: 12.5px; }
  .clear { border: 0; background: none; color: var(--text-faint); padding: 3px; border-radius: 4px; opacity: 0; }
  dd:hover .clear { opacity: 1; }
  .problem { font-size: 11.5px; color: var(--warn); white-space: nowrap; }
  .node-link { border: 0; background: none; color: var(--accent); padding: 4px 8px; text-decoration: underline; text-underline-offset: 3px; }
  .chips { flex: 1; min-width: 0; display: flex; flex-wrap: wrap; align-items: center; gap: 4px; }
  .chip { display: inline-flex; align-items: center; border: 1px solid var(--line-strong); border-radius: 6px; background: var(--bg-raised); }
  .chip .node-link { padding: 2px 4px 2px 8px; }
  .chip-text { padding: 2px 4px 2px 8px; font-size: 13.5px; }
  .chip-x { border: 0; background: none; color: var(--text-faint); padding: 4px 6px 4px 2px; display: grid; }
  .chip-x:hover { color: var(--text); }
  .add-item { flex: 1 1 120px; }
  .compact .grid { grid-template-columns: 110px 1fr; }
</style>
