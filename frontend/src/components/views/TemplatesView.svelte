<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { onChange } from '../../lib/events';
  import { openNode, whereFrom } from '../../lib/links.svelte';
  import { templatesStore } from '../../lib/templates.svelte';
  import { supertags } from '../../lib/supertags.svelte';
  import type { NodeDto } from '../../lib/types';
  import Icon from '../Icon.svelte';

  let name = $state('');
  onMount(() => { templatesStore.load(); return onChange((e) => { if (e.path?.startsWith('templates/') || e.oldPath?.startsWith('templates/')) templatesStore.load(); }); });

  async function create() {
    if (!name.trim()) return;
    try {
      const node = await api.post<NodeDto>('templates', { title: name.trim(), body: '## {{title}}\n\nCreated {{date}}.\n' });
      name = '';
      await templatesStore.load();
      openNode(node.id, 'new', node.title);
    } catch (e) { app.error(e); }
  }

  async function useTemplate(id: string, title: string) {
    try {
      const node = await api.post<NodeDto>('nodes', { title: `${title} ${new Date().toLocaleDateString()}`, workspace: app.workspace });
      await api.post(`nodes/${node.id}/apply-template`, { templateId: id });
      openNode(node.id, 'new');
    } catch (e) { app.error(e); }
  }

  const linkedTags = (id: string) => supertags.list.filter((t) => t.template === id);
</script>

<div class="scroll view">
  <div class="inner">
    <h1>Templates</h1>
    <p class="muted">Templates live in the vault's templates folder. Variables: <code>{'{{title}}'}</code>, <code>{'{{date}}'}</code>, <code>{'{{date:+7}}'}</code>, <code>{'{{time}}'}</code>, <code>{'{{weekday}}'}</code>, <code>{'{{workspace}}'}</code>. Properties in a template's frontmatter are copied into notes that don't have them yet.</p>
    <form class="new" onsubmit={(e) => { e.preventDefault(); create(); }}>
      <input class="input" bind:value={name} placeholder="New template name" /><button class="btn primary" type="submit">Create</button>
    </form>
    {#each templatesStore.list as t (t.id)}
      <div class="row">
        <Icon name="copy" size={16} />
        <button class="title" onclick={(e) => openNode(t.id, whereFrom(e), t.title)}>{t.title}</button>
        {#each linkedTags(t.id) as tag (tag.id)}<span class="badge accent">#{tag.name}{tag.autoApplyTemplate ? ' · auto' : ''}</span>{/each}
        <button class="btn" onclick={() => useTemplate(t.id, t.title)}>New note from template</button>
      </div>
    {:else}
      <div class="empty-state"><h3>No templates yet</h3></div>
    {/each}
  </div>
</div>

<style>
  .view { flex: 1; padding: 0 28px; }
  .inner { max-width: 860px; margin: 0 auto; padding: 24px 0 80px; }
  h1 { font: 700 28px/1.2 var(--font-display); }
  code { color: var(--accent); font-size: 12.5px; }
  .new { display: flex; gap: 8px; margin: 16px 0; }
  .row { display: flex; align-items: center; gap: 10px; padding: 10px 6px; border-bottom: 1px solid var(--line); color: var(--text-muted); }
  .title { flex: 1; text-align: left; border: 0; background: none; color: var(--text); font-weight: 600; font-size: 15px; }
  .title:hover { color: var(--accent); }
</style>
