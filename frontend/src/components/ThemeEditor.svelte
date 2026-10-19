<script lang="ts">
  import { app } from '../lib/state.svelte';
  import { prefs } from '../lib/prefs.svelte';
  import { instanceId } from '../lib/api';
  import { ACCENTS, themes, themeFileCss } from '../lib/appearance.svelte';
  import { DEFAULT_LOOK, FONTS, PRESETS, baseOf, lookVars, parseHex, toHex, type Look, type Preset } from '../lib/look';

  /** Settings › Appearance: theme presets, any background, text and accent colour, and fonts. */
  const look = $derived(prefs.look);
  const base = $derived(baseOf(prefs.look, app.theme));
  const vars = $derived(lookVars(prefs.look, base));

  function set(patch: Partial<Look>) { prefs.look = { ...prefs.look, ...patch }; }
  let scaleDraft = $state<number | null>(null);

  function applyPreset(p: Preset) {
    prefs.look = { ...DEFAULT_LOOK, scale: prefs.look.scale, ...p.look };
    if (p.base) app.theme = p.base;
  }
  const activePreset = $derived(PRESETS.find((p) =>
    (['bg', 'text', 'accent', 'fontUi', 'fontHeading', 'fontEditor', 'fontMono'] as const).every((k) => (p.look[k] ?? null) === (look[k] ?? null)) && (!p.base || p.base === app.theme))?.key ?? null);

  // What each preset looks like, for its card.
  function swatch(p: Preset) {
    const l = { ...DEFAULT_LOOK, ...p.look };
    const b = p.base ?? baseOf(l, 'dark');
    const v = lookVars({ ...l, accent: l.accent ?? (b === 'light' ? ACCENTS.blue.light : ACCENTS.blue.dark) }, b);
    return { bg: v['--bg'] ?? (b === 'light' ? '#f5f7fb' : '#0c131d'), panel: v['--bg-raised'] ?? (b === 'light' ? '#ffffff' : '#101824'), text: v['--text'] ?? (b === 'light' ? '#0c131d' : '#edf2fa'), accent: v['--accent'] };
  }

  /** A colour from the picker or typed as hex; anything else is ignored until it's complete. */
  function colour(key: 'bg' | 'text' | 'accent', value: string) {
    const rgb = parseHex(value);
    if (rgb) set({ [key]: toHex(rgb) });
  }
  const shown = (key: 'bg' | 'text' | 'accent') => look[key] ?? (key === 'bg' ? (app.theme === 'light' ? '#f5f7fb' : '#0c131d') : key === 'text' ? vars['--text'] ?? (base === 'light' ? '#0c131d' : '#edf2fa') : (base === 'light' ? ACCENTS[prefs.accent]?.light : ACCENTS[prefs.accent]?.dark) ?? '#78b6ff');

  const fontRoles = [['fontUi', 'Interface'], ['fontHeading', 'Headings'], ['fontEditor', 'Editor'], ['fontMono', 'Code']] as const;
  let other = $state<Record<string, boolean>>({});
  const isListed = (name: string | null) => !name || FONTS.some((f) => f.name === name);

  async function saveToVault() {
    const name = await app.ask('Save as a vault style', { message: 'Saved in the vault’s themes folder, so you can apply it from any device (Custom styles, below).', placeholder: 'Name, e.g. My theme', confirmLabel: 'Save' });
    if (!name?.trim()) return;
    const file = name.trim().replace(/[^\w -]/g, '').replace(/\s+/g, '-').toLowerCase().slice(0, 60) + '.css';
    try {
      const res = await fetch('/api/themes/' + encodeURIComponent(file), { method: 'PUT', credentials: 'same-origin', headers: { 'Content-Type': 'text/css', 'X-Quantos-Request': '1', 'X-Quantos-Instance': instanceId }, body: themeFileCss(name.trim(), prefs.look, app.theme) });
      if (!res.ok) throw new Error((await res.json().catch(() => null))?.error ?? res.statusText);
      await themes.list();
      app.toast(`Saved ${file}. Apply it under Custom styles on any device.`, 'ok');
    } catch (e) { app.error(e); }
  }
</script>

<div class="field top"><span>Theme</span>
  <div class="presets" role="radiogroup" aria-label="Theme">
    {#each PRESETS as p (p.key)}
      {@const s = swatch(p)}
      <button role="radio" aria-checked={activePreset === p.key} class="preset" class:on={activePreset === p.key} onclick={() => applyPreset(p)}
        style={`--pbg:${s.bg};--ppanel:${s.panel};--ptext:${s.text};--pacc:${s.accent}`}>
        <span class="sample"><span class="bar"></span><span class="line"></span><span class="line short"></span><span class="dot"></span></span>
        <span class="name">{p.label}</span>
      </button>
    {/each}
  </div>
</div>

<div class="field"><span>Background</span>
  <input type="color" aria-label="Background colour" value={shown('bg')} oninput={(e) => colour('bg', (e.target as HTMLInputElement).value)} />
  <input class="input hex" aria-label="Background hex" value={look.bg ?? ''} placeholder={app.theme === 'light' ? 'Built-in light' : 'Built-in dark'} onchange={(e) => { const v = (e.target as HTMLInputElement).value.trim(); if (!v) set({ bg: null }); else colour('bg', v); }} />
  {#if look.bg}<button class="btn ghost" onclick={() => set({ bg: null })}>Built-in</button>{:else}
    <div class="seg"><button class:on={app.theme === 'dark'} onclick={() => (app.theme = 'dark')}>Dark</button><button class:on={app.theme === 'light'} onclick={() => (app.theme = 'light')}>Light</button></div>{/if}
</div>
<div class="field"><span>Text</span>
  <input type="color" aria-label="Text colour" value={shown('text')} disabled={!look.bg} oninput={(e) => colour('text', (e.target as HTMLInputElement).value)} />
  <input class="input hex" aria-label="Text hex" value={look.text ?? ''} placeholder="Automatic" disabled={!look.bg} onchange={(e) => { const v = (e.target as HTMLInputElement).value.trim(); if (!v) set({ text: null }); else colour('text', v); }} />
  {#if look.text}<button class="btn ghost" onclick={() => set({ text: null })}>Automatic</button>{/if}
  {#if !look.bg}<span class="faint small">Choose a background to set the text colour.</span>{/if}
</div>
<div class="field"><span id="accent-label">Accent</span>
  <div class="swatches" role="radiogroup" aria-labelledby="accent-label">
    {#each Object.entries(ACCENTS) as [key, a] (key)}
      <button role="radio" aria-checked={!look.accent && prefs.accent === key} aria-label={a.label} title={a.label} class="swatch" class:on={!look.accent && prefs.accent === key}
        style="--c:{base === 'light' ? a.light : a.dark}" onclick={() => { prefs.accent = key; set({ accent: null }); }}></button>
    {/each}
  </div>
  <input type="color" aria-label="Any accent colour" title="Any colour" value={shown('accent')} oninput={(e) => colour('accent', (e.target as HTMLInputElement).value)} />
  <input class="input hex" aria-label="Accent hex" value={look.accent ?? ''} placeholder="#hex" onchange={(e) => { const v = (e.target as HTMLInputElement).value.trim(); if (!v) set({ accent: null }); else colour('accent', v); }} />
</div>
{#if look.accent && vars['--accent'] && vars['--accent'].toLowerCase() !== look.accent.toLowerCase()}
  <p class="faint small indent">Adjusted to {vars['--accent']} so text in that colour stays readable on this background.</p>
{/if}

{#each fontRoles as [key, label] (key)}
  <div class="field"><span>{label} font</span>
    {#if other[key] || !isListed(look[key])}
      <input class="input font" aria-label={`${label} font name`} placeholder="Any installed font's name" value={look[key] ?? ''} onchange={(e) => set({ [key]: (e.target as HTMLInputElement).value.trim() || null })} />
      <button class="btn ghost" onclick={() => { other[key] = false; set({ [key]: null }); }}>List</button>
    {:else}
      <select class="input font" aria-label={`${label} font`} value={look[key] ?? ''} onchange={(e) => { const v = (e.target as HTMLSelectElement).value; if (v === '*') other[key] = true; else set({ [key]: v || null }); }}>
        <option value="">Default</option>
        {#each FONTS as f (f.name)}<option value={f.name} style={`font-family:"${f.name}"`}>{f.name}</option>{/each}
        <option value="*">Another font…</option>
      </select>
    {/if}
    {#if look[key]}<span class="preview" style={`font-family:"${look[key]}"`}>Quantos Aa 123</span>{/if}
  </div>
{/each}
<!-- Applied when the slider is let go: resizing everything while dragging moves the slider under the pointer. -->
<div class="field"><span>Interface size</span><input type="range" aria-label="Interface size" min="0.9" max="1.25" step="0.05" value={look.scale}
  oninput={(e) => (scaleDraft = Number((e.target as HTMLInputElement).value))} onchange={(e) => { set({ scale: Number((e.target as HTMLInputElement).value) }); scaleDraft = null; }} />
  <span class="mono">{Math.round((scaleDraft ?? look.scale) * 100)}%</span>
  {#if look.scale !== 1}<button class="btn ghost" onclick={() => set({ scale: 1 })}>100%</button>{/if}</div>
<div class="row">
  <button class="btn" onclick={saveToVault}>Save as a vault style…</button>
  <button class="btn ghost" onclick={() => { prefs.look = { ...DEFAULT_LOOK }; prefs.accent = 'blue'; app.theme = 'dark'; }}>Reset theme</button>
</div>

<style>
  .field { display: flex; align-items: center; gap: 10px; margin: 10px 0; flex-wrap: wrap; }
  .field > span:first-child { width: 130px; color: var(--text-soft); flex: none; }
  .field.top { align-items: flex-start; }
  .presets { display: grid; grid-template-columns: repeat(auto-fill, minmax(110px, 1fr)); gap: 8px; flex: 1; min-width: 260px; max-width: 760px; }
  .preset { display: grid; gap: 4px; padding: 6px; border: 1px solid var(--line-strong); border-radius: 9px; background: var(--bg-raised); cursor: pointer; text-align: left; }
  .preset.on { border-color: var(--accent); box-shadow: 0 0 0 1px var(--accent); }
  .sample { position: relative; display: block; height: 52px; border-radius: 6px; background: var(--pbg); overflow: hidden; border: 1px solid color-mix(in srgb, var(--ptext) 15%, transparent); }
  .sample .bar { position: absolute; left: 0; top: 0; bottom: 0; width: 26%; background: var(--ppanel); }
  .sample .line { position: absolute; left: 34%; top: 14px; width: 52%; height: 5px; border-radius: 3px; background: var(--ptext); opacity: .85; }
  .sample .line.short { top: 26px; width: 34%; opacity: .5; }
  .sample .dot { position: absolute; right: 8px; bottom: 8px; width: 12px; height: 12px; border-radius: 50%; background: var(--pacc); }
  .name { font-size: 12px; color: var(--text-soft); }
  input[type='color'] { width: 34px; height: 28px; padding: 0; border: 1px solid var(--line-strong); border-radius: 6px; background: none; cursor: pointer; }
  input[type='color']:disabled { opacity: .4; cursor: default; }
  .hex { width: 110px; font-family: var(--font-mono); }
  .font { width: 220px; }
  .preview { font-size: 15px; color: var(--text-soft); }
  .swatches { display: flex; gap: 6px; }
  .swatch { width: 22px; height: 22px; border-radius: 50%; background: var(--c); border: 2px solid transparent; cursor: pointer; padding: 0; }
  .swatch.on { border-color: var(--text); }
  .seg { display: inline-flex; border: 1px solid var(--line-strong); border-radius: 7px; overflow: hidden; }
  .seg button { border: 0; background: none; color: var(--text-muted); padding: 5px 10px; font: 13px var(--font-body); cursor: pointer; }
  .seg button + button { border-left: 1px solid var(--line-strong); }
  .seg button.on { background: var(--accent-wash); color: var(--accent-strong); }
  .small { font-size: 12.5px; }
  .indent { margin: -4px 0 6px 140px; }
  .mono { font-family: var(--font-mono); font-size: 12.5px; }
  .row { display: flex; gap: 8px; margin: 14px 0 6px; }
</style>
