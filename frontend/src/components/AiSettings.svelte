<script lang="ts">
  import { onMount } from 'svelte';
  import { ai, type AiEndpoint, type AiSettings } from '../lib/ai.svelte';
  import { app } from '../lib/state.svelte';

  /** Settings › AI (Release 1.1): one provider for writing and answering, one for meaning search. */
  let draft = $state<AiSettings>({ enabled: false, chat: null, embeddings: null });
  let keyDraft = $state<Record<string, string>>({});
  let models = $state<Record<string, string[]>>({});
  let busy = $state('');
  let poll: ReturnType<typeof setInterval>;

  onMount(() => {
    ai.load().then(() => (draft = structuredClone($state.snapshot(ai.settings))));
    poll = setInterval(() => { if (ai.index?.working || ai.index?.pending) ai.load(); }, 3000);
    return () => clearInterval(poll);
  });

  const dirty = $derived(JSON.stringify(draft) !== JSON.stringify(ai.settings));

  function choose(part: 'chat' | 'embeddings', provider: string) {
    const p = ai.preset(provider);
    draft[part] = provider ? { provider, baseUrl: p?.baseUrl ?? null, model: (part === 'chat' ? p?.defaultChatModel : p?.defaultEmbeddingModel) ?? '' } : null;
  }

  async function listModels(part: 'chat' | 'embeddings') {
    const e = draft[part];
    if (!e) return;
    busy = 'models-' + part;
    try { models[part] = await ai.listModels(e.provider, e.baseUrl); if (!models[part].length) app.toast('The provider listed no models.', 'warn'); }
    catch (err) { app.error(err); }
    finally { busy = ''; }
  }

  async function saveKey(provider: string) {
    busy = 'key';
    try { await ai.setKey(provider, keyDraft[provider] ?? ''); keyDraft[provider] = ''; app.toast('Key saved, encrypted for your Windows account.', 'ok'); }
    catch (err) { app.error(err); }
    finally { busy = ''; }
  }

  async function save() {
    busy = 'save';
    try { await ai.save($state.snapshot(draft) as AiSettings); app.toast(draft.enabled ? 'AI settings saved.' : 'AI is off.', 'ok'); }
    catch (err) { app.error(err); }
    finally { busy = ''; }
  }

  function privacy(e: AiEndpoint | null, what: string) {
    if (!e) return '';
    const p = ai.preset(e.provider);
    const local = /^https?:\/\/(localhost|127\.0\.0\.1|\[::1\])(:|\/|$)/i.test(e.baseUrl || p?.baseUrl || '');
    return local ? 'Runs on this computer: nothing leaves it.' : `${what} are sent to ${p?.name ?? 'this provider'} when you use AI. Nothing is sent otherwise.`;
  }
</script>

<h2>AI</h2>
<p class="faint intro">Off unless you switch it on. Providers marked “on this computer” (Ollama, LM Studio) keep everything local. With any other provider, only what a request needs is sent: your question and the passages of notes it draws on, or the text you selected. Keys are encrypted for your Windows account and never stored in the vault.</p>

<label class="check master"><input type="checkbox" bind:checked={draft.enabled} /> Use AI features</label>

{#each [['chat', 'Writing and answering', 'Ask your notes, and rewrite, summarise or continue selected text.', 'Questions, the notes they draw on and selected text'], ['embeddings', 'Meaning search', 'Finds notes by what they mean, not just the words in them. Notes are indexed in the background.', 'Note passages (while indexing) and search phrases']] as [part, title, help, sent] (part)}
  {@const key = part as 'chat' | 'embeddings'}
  {@const e = draft[key]}
  {@const p = ai.preset(e?.provider)}
  <section class:off={!draft.enabled}>
    <h3>{title}</h3>
    <p class="faint">{help}</p>
    <div class="field"><span>Provider</span>
      <select class="input" aria-label="{title} provider" value={e?.provider ?? ''} onchange={(ev) => choose(key, (ev.target as HTMLSelectElement).value)}>
        <option value="">None</option>
        {#each ai.presets.filter((x) => (key === 'chat' ? x.chat : x.embeddings)) as x (x.id)}<option value={x.id}>{x.name}</option>{/each}
      </select></div>
    {#if e && p}
      <p class="faint note">{p.note}</p>
      <div class="field"><span>Address</span><input class="input" aria-label="{title} address" bind:value={e.baseUrl} placeholder={p.baseUrl} /></div>
      <div class="field"><span>Model</span>
        <input class="input" aria-label="{title} model" list="models-{key}" bind:value={e.model} placeholder="model name" />
        <datalist id="models-{key}">{#each models[key] ?? [] as m (m)}<option value={m}></option>{/each}</datalist>
        <button class="btn ghost" disabled={busy === 'models-' + key} onclick={() => listModels(key)}>{busy === 'models-' + key ? 'Asking…' : 'List models'}</button></div>
      {#if p.needsKey || e.provider === 'custom'}
        <div class="field"><span>API key</span>
          <input class="input" type="password" autocomplete="off" aria-label="{p.name} API key" bind:value={keyDraft[e.provider]}
            placeholder={ai.keys.includes(e.provider) ? 'Saved. Type a new key to replace it' : 'Paste your key'} />
          <button class="btn ghost" disabled={busy === 'key' || !keyDraft[e.provider]} onclick={() => saveKey(e.provider)}>Save key</button>
          {#if ai.keys.includes(e.provider)}<button class="btn ghost" onclick={() => ai.setKey(e.provider, null)}>Remove</button>{/if}
        </div>
      {/if}
      <p class="privacy" class:local={privacy(e, sent).startsWith('Runs on this computer')}>{privacy(e, sent)}</p>
    {/if}
  </section>
{/each}

<div class="row">
  <button class="btn primary" disabled={busy === 'save' || !dirty} onclick={save}>Save</button>
  {#if dirty}<span class="faint">Unsaved changes</span>{/if}
</div>

{#if ai.settings.enabled && ai.settings.embeddings && ai.index}
  <h3>Meaning search index</h3>
  <p>{ai.index.indexed} of {ai.index.notes} notes indexed{ai.index.working ? ` · indexing ${ai.index.pending} note(s)…` : ''}</p>
  {#if ai.index.error}<p class="warn">{ai.index.error}</p>{/if}
  <button class="btn ghost" onclick={async () => { await fetch('/api/ai/reindex', { method: 'POST', credentials: 'same-origin', headers: { 'X-Quantos-Request': '1' } }); setTimeout(() => ai.load(), 1500); }}>Check for changes now</button>
{/if}

<style>
  h2 { font: 700 24px/1.2 var(--font-display); margin-bottom: 8px; }
  h3 { font: 700 16px/1.3 var(--font-display); margin: 22px 0 4px; }
  .intro { max-width: 680px; font-size: 13.5px; }
  .master { margin: 14px 0 4px; font-weight: 600; }
  section.off { opacity: .6; }
  .field { display: flex; align-items: center; gap: 10px; margin: 8px 0; }
  .field > span:first-child { width: 110px; color: var(--text-soft); flex: none; }
  .field .input { max-width: 420px; }
  .note { font-size: 13px; max-width: 680px; }
  .privacy { font-size: 13px; color: var(--warn); max-width: 680px; }
  .privacy.local { color: var(--ok); }
  .row { display: flex; gap: 10px; align-items: center; margin-top: 18px; }
  .check { display: flex; gap: 8px; align-items: center; }
</style>
