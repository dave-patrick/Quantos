<script lang="ts" module>
  export type ActionKind = 'set' | 'addTag' | 'notify' | 'createTask' | 'archive';
  export interface UiAction { kind: ActionKind; key: string; value: string; due: string }
  export interface UiRule {
    id?: string; name: string; enabled: boolean;
    when: { type: string; tag: string; property: string; from: string; to: string };
    then: UiAction[];
  }
  type Json = Record<string, any>;

  const blankAction = (kind: ActionKind = 'set'): UiAction => ({ kind, key: '', value: '', due: '' });
  export const blankRule = (): UiRule => ({ name: '', enabled: true, when: { type: '', tag: '', property: 'status', from: '', to: '' }, then: [blankAction()] });

  /** automations.json rules to editable rows; an action with several parts becomes several rows. */
  export function fromJson(list: Json[]): UiRule[] {
    return list.map((r) => ({
      id: r.id, name: r.name ?? '', enabled: r.enabled !== false,
      when: { type: r.when?.type ?? '', tag: r.when?.tag ?? '', property: r.when?.property ?? '', from: r.when?.from ?? '', to: r.when?.to ?? '' },
      then: (r.then ?? []).flatMap((a: Json): UiAction[] => [
        ...Object.entries(a.set ?? {}).map(([key, value]) => ({ ...blankAction('set'), key, value: String(value) })),
        ...(a.addTag ? [{ ...blankAction('addTag'), value: a.addTag }] : []),
        ...(a.notify ? [{ ...blankAction('notify'), value: a.notify }] : []),
        ...(a.createTask ? [{ ...blankAction('createTask'), value: a.createTask.title ?? '', due: a.createTask.due ?? '' }] : []),
        ...(a.archive ? [blankAction('archive')] : [])
      ])
    }));
  }

  export function toJson(rules: UiRule[]): Json[] {
    const opt = (v: string) => (v.trim() ? v.trim() : undefined);
    return rules.map((r) => ({
      ...(r.id ? { id: r.id } : {}), name: r.name.trim(), ...(r.enabled ? {} : { enabled: false }),
      when: { type: opt(r.when.type), tag: opt(r.when.tag.replace(/^#/, '')), property: r.when.property.trim(), from: opt(r.when.from), to: opt(r.when.to) },
      then: r.then.flatMap((a): Json[] => {
        switch (a.kind) {
          case 'set': return a.key.trim() ? [{ set: { [a.key.trim()]: a.value } }] : [];
          case 'addTag': return a.value.trim() ? [{ addTag: a.value.trim().replace(/^#/, '') }] : [];
          case 'notify': return a.value.trim() ? [{ notify: a.value.trim() }] : [];
          case 'createTask': return [{ createTask: { title: a.value.trim() || 'Follow up on {{title}}', ...(a.due.trim() ? { due: a.due.trim() } : {}) } }];
          case 'archive': return [{ archive: true }];
        }
      })
    }));
  }

  /** What's missing, per rule, before saving. */
  export function ruleProblems(rules: UiRule[]): string[] {
    return rules.flatMap((r, i) => {
      const label = r.name.trim() || `Automation ${i + 1}`;
      const out: string[] = [];
      if (!r.name.trim()) out.push(`${label}: give it a name.`);
      if (!r.when.property.trim()) out.push(`${label}: choose the property to watch.`);
      if (!r.then.length) out.push(`${label}: add at least one action.`);
      return out;
    });
  }
</script>

<script lang="ts">
  import Icon from './Icon.svelte';
  let { rules = $bindable() }: { rules: UiRule[] } = $props();

  const kinds: { kind: ActionKind; label: string }[] = [
    { kind: 'set', label: 'Set a property' }, { kind: 'addTag', label: 'Add a tag' }, { kind: 'notify', label: 'Send a notification' },
    { kind: 'createTask', label: 'Create a task' }, { kind: 'archive', label: 'Archive the note' }
  ];
</script>

<div class="rules">
  {#each rules as rule, ri (ri)}
    <fieldset class="rule" class:off={!rule.enabled}>
      <legend class="visually-hidden">Automation {ri + 1}</legend>
      <div class="head">
        <input class="input name" aria-label="Automation name" placeholder="Name, e.g. Stamp finished projects" bind:value={rule.name} />
        <label class="check"><input type="checkbox" bind:checked={rule.enabled} /> On</label>
        <button class="icon-btn" aria-label="Delete automation" title="Delete automation" onclick={() => (rules = rules.filter((_, i) => i !== ri))}><Icon name="trash" size={14} /></button>
      </div>
      <div class="when">
        <span class="faint">When a</span>
        <select class="input" aria-label="Kind of note" bind:value={rule.when.type}>
          <option value="">note or task</option><option value="note">note</option><option value="task">task</option><option value="person">person</option><option value="daily">daily note</option>
        </select>
        <span class="faint">tagged</span>
        <input class="input short" aria-label="Tag (optional)" placeholder="any tag" bind:value={rule.when.tag} />
        <span class="faint">has</span>
        <input class="input short" aria-label="Property" placeholder="status" bind:value={rule.when.property} />
        <span class="faint">change from</span>
        <input class="input short" aria-label="From value (optional)" placeholder="anything" bind:value={rule.when.from} />
        <span class="faint">to</span>
        <input class="input short" aria-label="To value (optional)" placeholder="anything" bind:value={rule.when.to} />
      </div>
      <ol class="then">
        {#each rule.then as action, ai (ai)}
          <li>
            <select class="input" aria-label="Action" bind:value={action.kind}>
              {#each kinds as k (k.kind)}<option value={k.kind}>{k.label}</option>{/each}
            </select>
            {#if action.kind === 'set'}
              <input class="input short" aria-label="Property to set" placeholder="finished_on" bind:value={action.key} />
              <span class="faint">to</span>
              <input class="input wide" aria-label="Value" placeholder={'{{today}}'} bind:value={action.value} />
            {:else if action.kind === 'addTag'}
              <input class="input short" aria-label="Tag to add" placeholder="done" bind:value={action.value} />
            {:else if action.kind === 'notify'}
              <input class="input wide" aria-label="Notification text" placeholder={'{{title}} is finished'} bind:value={action.value} />
            {:else if action.kind === 'createTask'}
              <input class="input wide" aria-label="Task title" placeholder={'Write a retro for {{title}}'} bind:value={action.value} />
              <input class="input short" aria-label="Task due date" placeholder={'due {{today+7}}'} bind:value={action.due} />
            {/if}
            <button class="icon-btn" aria-label="Remove action" title="Remove action" onclick={() => (rule.then = rule.then.filter((_, i) => i !== ai))}><Icon name="x" size={13} /></button>
          </li>
        {/each}
      </ol>
      <button class="btn ghost small" onclick={() => (rule.then = [...rule.then, blankAction()])}><Icon name="plus" size={13} /> Add an action</button>
    </fieldset>
  {/each}
  <button class="btn" onclick={() => (rules = [...rules, blankRule()])}><Icon name="plus" size={14} /> New automation</button>
  <p class="faint hint">In values, <code>{'{{title}}'}</code> is the note's title, <code>{'{{today}}'}</code> today's date (<code>{'{{today+7}}'}</code> a week on) and <code>{'{{now}}'}</code> the time. Leave from or to empty to match any value.</p>
</div>

<style>
  .rules { display: flex; flex-direction: column; gap: 10px; align-items: flex-start; }
  .rule { width: 100%; border: 1px solid var(--line-strong); border-radius: 10px; padding: 10px 12px; display: flex; flex-direction: column; gap: 8px; margin: 0; }
  .rule.off { opacity: .65; }
  .head, .when, .then li { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; }
  .rule .input { width: auto; }
  select.input { padding-right: 26px; }
  .name { flex: 1; min-width: 14em; font-weight: 600; }
  .rule .short { width: 8.5em; }
  .rule .wide { flex: 1; min-width: 6em; }
  .then li { flex-wrap: nowrap; }
  .then { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 6px; }
  .then li::before { content: 'then'; color: var(--text-faint); font-size: 12.5px; margin-right: 2px; }
  .small { height: 26px; font-size: 12.5px; }
  .hint { font-size: 12.5px; }
  .check { display: inline-flex; align-items: center; gap: 4px; font-size: 13px; }
</style>
