<script lang="ts">
  import { app } from '../lib/state.svelte';
  import { prefs } from '../lib/prefs.svelte';
  import { instanceId } from '../lib/api';
  import { ACCENTS, themes, themeFileCss } from '../lib/appearance.svelte';
  import {
    DEFAULT_LOOK, FONTS, PRESETS, baseOf, lookVars, lookCss, parseHex, toHex,
    THEME_ELEMENTS, effectiveColor, resetElements,
    type Look, type Preset, type ElementCategory
  } from '../lib/look';
  import Icon from './Icon.svelte';

  /** Settings › Appearance: theme presets, base colours, granular interface elements, and fonts. */
  const look = $derived(prefs.look);
  const base = $derived(baseOf(prefs.look, app.theme));
  const vars = $derived(lookVars(prefs.look, base));

  let scaleDraft = $state<number | null>(null);
  let activeCategory = $state<'all' | ElementCategory>('all');
  let copied = $state(false);

  function set(patch: Partial<Look>) { prefs.look = { ...prefs.look, ...patch }; }

  function applyPreset(p: Preset) {
    prefs.look = { ...DEFAULT_LOOK, scale: prefs.look.scale, ...p.look };
    if (p.base) app.theme = p.base;
  }

  const activePreset = $derived(
    PRESETS.find((p) =>
      (['bg', 'text', 'accent', 'fontUi', 'fontHeading', 'fontEditor', 'fontMono'] as const).every(
        (k) => (p.look[k] ?? null) === (look[k] ?? null)
      ) && (!p.base || p.base === app.theme)
    )?.key ?? null
  );

  function swatch(p: Preset) {
    const l = { ...DEFAULT_LOOK, ...p.look };
    const b = p.base ?? baseOf(l, 'dark');
    const v = lookVars({ ...l, accent: l.accent ?? (b === 'light' ? ACCENTS.blue.light : ACCENTS.blue.dark) }, b);
    return {
      bg: v['--bg'] ?? (b === 'light' ? '#f5f7fb' : '#2a2b2d'),
      panel: v['--sidebar-bg'] ?? v['--bg-panel'] ?? (b === 'light' ? '#eef2f8' : '#202123'),
      text: v['--text'] ?? (b === 'light' ? '#0c131d' : '#edf2fa'),
      accent: v['--accent'] ?? (b === 'light' ? '#1a66c4' : '#78b6ff')
    };
  }

  function colour(key: keyof Look, value: string) {
    const rgb = parseHex(value);
    if (rgb) set({ [key]: toHex(rgb) });
  }

  function clearElement(key: keyof Look) {
    set({ [key]: null });
  }

  const shown = (key: 'bg' | 'text' | 'accent') =>
    look[key] ??
    (key === 'bg'
      ? app.theme === 'light' ? '#f5f7fb' : '#2a2b2d'
      : key === 'text'
        ? vars['--text'] ?? (base === 'light' ? '#0c131d' : '#edf2fa')
        : (base === 'light' ? ACCENTS[prefs.accent]?.light : ACCENTS[prefs.accent]?.dark) ?? '#78b6ff');

  const categories: { id: 'all' | ElementCategory; label: string }[] = [
    { id: 'all', label: 'All elements' },
    { id: 'surfaces', label: 'Surfaces' },
    { id: 'interactive', label: 'Interactive' },
    { id: 'borders', label: 'Borders' },
    { id: 'text', label: 'Text' },
    { id: 'feedback', label: 'Feedback' }
  ];

  const filteredElements = $derived(
    activeCategory === 'all' ? THEME_ELEMENTS : THEME_ELEMENTS.filter((e) => e.category === activeCategory)
  );

  const customCounts = $derived(
    THEME_ELEMENTS.reduce(
      (acc, el) => {
        if (look[el.key] != null) acc[el.category] = (acc[el.category] || 0) + 1;
        return acc;
      },
      {} as Record<string, number>
    )
  );

  const totalCustomElements = $derived(THEME_ELEMENTS.filter((el) => look[el.key] != null).length);

  const fontRoles = [
    ['fontUi', 'Interface'],
    ['fontHeading', 'Headings'],
    ['fontEditor', 'Editor'],
    ['fontMono', 'Code']
  ] as const;

  let other = $state<Record<string, boolean>>({});
  const isListed = (name: string | null) => !name || FONTS.some((f) => f.name === name);

  function autoBalance() {
    prefs.look = resetElements(prefs.look);
    app.toast('Element overrides reset to automatic palette', 'ok');
  }

  async function copyCss() {
    const css = lookCss(prefs.look, base);
    if (!css) {
      app.toast('Default theme has no custom CSS overrides', 'ok');
      return;
    }
    try {
      await navigator.clipboard.writeText(css);
      copied = true;
      setTimeout(() => (copied = false), 2000);
      app.toast('Theme CSS copied to clipboard', 'ok');
    } catch {
      app.toast('Could not copy CSS to clipboard', 'error');
    }
  }

  async function saveToVault() {
    const name = await app.ask('Save as a vault style', {
      message: 'Saved in the vault’s themes folder, so you can apply it from any device (Custom styles, below).',
      placeholder: 'Name, e.g. My theme',
      confirmLabel: 'Save'
    });
    if (!name?.trim()) return;
    const file = name.trim().replace(/[^\w -]/g, '').replace(/\s+/g, '-').toLowerCase().slice(0, 60) + '.css';
    try {
      const res = await fetch('/api/themes/' + encodeURIComponent(file), {
        method: 'PUT',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'text/css', 'X-Quantos-Request': '1', 'X-Quantos-Instance': instanceId },
        body: themeFileCss(name.trim(), prefs.look, app.theme)
      });
      if (!res.ok) throw new Error((await res.json().catch(() => null))?.error ?? res.statusText);
      await themes.list();
      app.toast(`Saved ${file}. Apply it under Custom styles on any device.`, 'ok');
    } catch (e) {
      app.error(e);
    }
  }
</script>

<div class="field">
  <span>Theme</span>
  <select
    class="input font"
    aria-label="Theme"
    value={activePreset ?? ''}
    onchange={(e) => {
      const preset = PRESETS.find((p) => p.key === (e.target as HTMLSelectElement).value);
      if (preset) applyPreset(preset);
    }}
  >
    {#if !activePreset}<option value="">Custom theme</option>{/if}
    {#each PRESETS as p (p.key)}
      <option value={p.key}>{p.label}</option>
    {/each}
  </select>
</div>

<div class="field">
  <span>Interface size</span>
  <input
    type="range"
    aria-label="Interface size"
    min="0.9"
    max="1.25"
    step="0.05"
    value={look.scale}
    oninput={(e) => (scaleDraft = Number((e.target as HTMLInputElement).value))}
    onchange={(e) => {
      set({ scale: Number((e.target as HTMLInputElement).value) });
      scaleDraft = null;
    }}
  />
  <span class="mono">{Math.round((scaleDraft ?? look.scale) * 100)}%</span>
  {#if look.scale !== 1}
    <button class="btn ghost" onclick={() => set({ scale: 1 })}>100%</button>
  {/if}
</div>

<details class="advanced" open>
  <summary>Theme colours, elements and fonts</summary>

  <!-- Live interface mockup preview -->
  <div class="preview-card-wrap">
    <div class="preview-header-label">
      <span>Live Theme Preview</span>
      <span class="preview-badge">{base === 'light' ? 'Light' : 'Dark'} base</span>
    </div>
    <div class="theme-mini-mockup">
      <!-- Mini Header -->
      <div class="mock-header">
        <div class="mock-brand">
          <span class="mock-dot"></span>
          <span class="mock-title">Quantos</span>
        </div>
        <div class="mock-search">Search notes…</div>
        <div class="mock-actions">
          <span class="mock-icon-btn"></span>
          <span class="mock-icon-btn"></span>
        </div>
      </div>

      <!-- Mini Body: Sidebar + Editor + Panel -->
      <div class="mock-body">
        <!-- Mini Sidebar -->
        <div class="mock-sidebar">
          <div class="mock-nav-item active">
            <span class="mock-nav-bullet"></span> Notes
          </div>
          <div class="mock-nav-item">
            <span class="mock-nav-bullet"></span> Daily
          </div>
          <div class="mock-nav-item">
            <span class="mock-nav-bullet"></span> Tasks
          </div>
          <div class="mock-nav-item">
            <span class="mock-nav-bullet"></span> Graph
          </div>
        </div>

        <!-- Mini Editor -->
        <div class="mock-editor">
          <div class="mock-note-title">Project Overview</div>
          <p class="mock-text">Quantos markdown notes with real-time reactive theme styling.</p>
          <div class="mock-elements-row">
            <span class="mock-wikilink">[[Project Plan]]</span>
            <span class="mock-tag">#active</span>
            <span class="mock-task"><span class="mock-check done">✓</span> Review colour palette</span>
          </div>
        </div>

        <!-- Mini Right Card -->
        <div class="mock-card">
          <div class="mock-card-head">Details</div>
          <div class="mock-card-row">Status: <span class="mock-ok-text">● Live</span></div>
          <div class="mock-card-row muted">Modified today</div>
        </div>
      </div>

      <!-- Mini Statusbar -->
      <div class="mock-statusbar">
        <span>● Connected</span>
        <span class="mock-spacer"></span>
        <span>Ready</span>
      </div>
    </div>
  </div>

  <!-- Presets Bar -->
  <div class="field top">
    <span>Theme presets</span>
    <div class="presets" role="radiogroup" aria-label="Theme">
      {#each PRESETS as p (p.key)}
        {@const s = swatch(p)}
        <button
          role="radio"
          aria-checked={activePreset === p.key}
          class="preset"
          class:on={activePreset === p.key}
          onclick={() => applyPreset(p)}
          style={`--pbg:${s.bg};--ppanel:${s.panel};--ptext:${s.text};--pacc:${s.accent}`}
        >
          <span class="sample">
            <span class="bar"></span>
            <span class="line"></span>
            <span class="line short"></span>
            <span class="dot"></span>
          </span>
          <span class="name">{p.label}</span>
        </button>
      {/each}
    </div>
  </div>

  <!-- Core Base Colours -->
  <div class="section-title">Base palette</div>

  <div class="field">
    <span>Background</span>
    <input
      type="color"
      aria-label="Background colour"
      value={shown('bg')}
      oninput={(e) => colour('bg', (e.target as HTMLInputElement).value)}
    />
    <input
      class="input hex"
      aria-label="Background hex"
      value={look.bg ?? ''}
      placeholder={app.theme === 'light' ? 'Built-in light' : 'Built-in dark'}
      onchange={(e) => {
        const v = (e.target as HTMLInputElement).value.trim();
        if (!v) set({ bg: null });
        else colour('bg', v);
      }}
    />
    {#if look.bg}
      <button class="btn ghost small-btn" onclick={() => set({ bg: null })}>Built-in</button>
    {:else}
      <div class="seg">
        <button class:on={app.theme === 'dark'} onclick={() => (app.theme = 'dark')}>Dark</button>
        <button class:on={app.theme === 'light'} onclick={() => (app.theme = 'light')}>Light</button>
      </div>
    {/if}
  </div>

  <div class="field">
    <span>Text</span>
    <input
      type="color"
      aria-label="Text colour"
      value={shown('text')}
      disabled={!look.bg}
      oninput={(e) => colour('text', (e.target as HTMLInputElement).value)}
    />
    <input
      class="input hex"
      aria-label="Text hex"
      value={look.text ?? ''}
      placeholder="Automatic"
      disabled={!look.bg}
      onchange={(e) => {
        const v = (e.target as HTMLInputElement).value.trim();
        if (!v) set({ text: null });
        else colour('text', v);
      }}
    />
    {#if look.text}
      <button class="btn ghost small-btn" onclick={() => set({ text: null })}>Automatic</button>
    {/if}
    {#if !look.bg}
      <span class="faint small">Choose a custom background to override primary text colour.</span>
    {/if}
  </div>

  <div class="field">
    <span id="accent-label">Accent</span>
    <div class="swatches" role="radiogroup" aria-labelledby="accent-label">
      {#each Object.entries(ACCENTS) as [key, a] (key)}
        <button
          role="radio"
          aria-checked={!look.accent && prefs.accent === key}
          aria-label={a.label}
          title={a.label}
          class="swatch"
          class:on={!look.accent && prefs.accent === key}
          style="--c:{base === 'light' ? a.light : a.dark}"
          onclick={() => {
            prefs.accent = key;
            set({ accent: null });
          }}
        ></button>
      {/each}
    </div>
    <input
      type="color"
      aria-label="Any accent colour"
      title="Any colour"
      value={shown('accent')}
      oninput={(e) => colour('accent', (e.target as HTMLInputElement).value)}
    />
    <input
      class="input hex"
      aria-label="Accent hex"
      value={look.accent ?? ''}
      placeholder="#hex"
      onchange={(e) => {
        const v = (e.target as HTMLInputElement).value.trim();
        if (!v) set({ accent: null });
        else colour('accent', v);
      }}
    />
  </div>
  {#if look.accent && vars['--accent'] && vars['--accent'].toLowerCase() !== look.accent.toLowerCase()}
    <p class="faint small indent">Adjusted to {vars['--accent']} so text in that colour stays readable on this background.</p>
  {/if}

  <!-- Granular Interface Element Customisation -->
  <div class="elements-section">
    <div class="elements-header">
      <div class="elements-title-row">
        <span class="section-title">Interface element colours</span>
        {#if totalCustomElements > 0}
          <span class="custom-badge">{totalCustomElements} custom</span>
          <button class="btn ghost small-btn" onclick={autoBalance}>Reset elements to auto</button>
        {:else}
          <span class="auto-badge">All automatic</span>
        {/if}
      </div>
      <p class="faint small">Pick exact colours for individual parts of the interface, or leave them automatic.</p>
    </div>

    <!-- Category filter tabs -->
    <div class="category-tabs" role="tablist" aria-label="Element categories">
      {#each categories as cat (cat.id)}
        {@const count = cat.id === 'all' ? totalCustomElements : customCounts[cat.id] ?? 0}
        <button
          role="tab"
          aria-selected={activeCategory === cat.id}
          class="cat-tab"
          class:active={activeCategory === cat.id}
          onclick={() => (activeCategory = cat.id)}
        >
          {cat.label}
          {#if count > 0}<span class="tab-count">{count}</span>{/if}
        </button>
      {/each}
    </div>

    <!-- Element Colour Rows -->
    <div class="elements-grid">
      {#each filteredElements as el (el.key)}
        {@const val = look[el.key] ?? null}
        {@const resolved = effectiveColor(el.cssVar, look, base)}
        <div class="element-row" class:is-custom={val != null}>
          <div class="el-info">
            <span class="el-label">{el.label}</span>
            <span class="el-desc">{el.description}</span>
          </div>

          <div class="el-controls">
            <input
              type="color"
              aria-label={`${el.label} colour`}
              value={val ?? resolved}
              oninput={(e) => colour(el.key, (e.target as HTMLInputElement).value)}
            />
            <input
              class="input hex"
              aria-label={`${el.label} hex`}
              value={val ?? ''}
              placeholder={resolved}
              onchange={(e) => {
                const v = (e.target as HTMLInputElement).value.trim();
                if (!v) clearElement(el.key);
                else colour(el.key, v);
              }}
            />
            {#if val != null}
              <button class="btn ghost small-btn" title="Reset to automatic colour" onclick={() => clearElement(el.key)}>
                Auto
              </button>
            {:else}
              <span class="status-pill auto">Auto</span>
            {/if}
          </div>
        </div>
      {/each}
    </div>
  </div>

  <!-- Typefaces & Fonts -->
  <div class="section-title">Fonts</div>
  {#each fontRoles as [key, label] (key)}
    <div class="field">
      <span>{label} font</span>
      {#if other[key] || !isListed(look[key])}
        <input
          class="input font"
          aria-label={`${label} font name`}
          placeholder="Any installed font's name"
          value={look[key] ?? ''}
          onchange={(e) => set({ [key]: (e.target as HTMLInputElement).value.trim() || null })}
        />
        <button class="btn ghost small-btn" onclick={() => { other[key] = false; set({ [key]: null }); }}>List</button>
      {:else}
        <select
          class="input font"
          aria-label={`${label} font`}
          value={look[key] ?? ''}
          onchange={(e) => {
            const v = (e.target as HTMLSelectElement).value;
            if (v === '*') other[key] = true;
            else set({ [key]: v || null });
          }}
        >
          <option value="">Default</option>
          {#each FONTS as f (f.name)}
            <option value={f.name} style={`font-family:"${f.name}"`}>{f.name}</option>
          {/each}
          <option value="*">Another font…</option>
        </select>
      {/if}
      {#if look[key]}
        <span class="preview" style={`font-family:"${look[key]}"`}>Quantos Aa 123</span>
      {/if}
    </div>
  {/each}

  <!-- Action buttons -->
  <div class="row actions-row">
    <button class="btn" onclick={saveToVault}>Save as a vault style…</button>
    <button class="btn ghost" onclick={copyCss}>
      {#if copied}<Icon name="check" size={14} /> Copied!{:else}<Icon name="copy" size={14} /> Copy CSS{/if}
    </button>
    {#if totalCustomElements > 0}
      <button class="btn ghost" onclick={autoBalance}>Reset elements to auto</button>
    {/if}
    <button
      class="btn ghost danger-btn"
      onclick={() => {
        prefs.look = { ...DEFAULT_LOOK };
        prefs.accent = 'blue';
        app.theme = 'dark';
        app.toast('Theme reset to default', 'ok');
      }}
    >
      Reset all
    </button>
  </div>
</details>

<style>
  .advanced { margin: 14px 0; }
  summary { cursor: pointer; color: var(--text-soft); min-height: 48px; padding: 8px 0; font-weight: 600; font-size: 15px; }
  .section-title { font-weight: 600; font-size: 13.5px; color: var(--text); margin: 16px 0 8px; }
  .field { display: flex; align-items: center; gap: 10px; margin: 10px 0; flex-wrap: wrap; }
  .field > span:first-child { width: 130px; max-width: 100%; color: var(--text-soft); flex: none; font-size: 13.5px; }
  .field.top { align-items: flex-start; }

  /* Preview mockup */
  .preview-card-wrap {
    margin: 12px 0 20px;
    padding: 12px;
    background: var(--bg-raised);
    border: 1px solid var(--line);
    border-radius: 12px;
  }
  .preview-header-label {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 8px;
    font-size: 12px;
    font-weight: 600;
    color: var(--text-muted);
    text-transform: uppercase;
    letter-spacing: 0.05em;
  }
  .preview-badge {
    font-size: 11px;
    padding: 2px 7px;
    border-radius: 10px;
    background: var(--bg-panel);
    border: 1px solid var(--line);
    color: var(--text-soft);
  }
  .theme-mini-mockup {
    display: flex;
    flex-direction: column;
    height: 170px;
    border-radius: 9px;
    background: var(--bg);
    border: 1px solid var(--line-strong);
    overflow: hidden;
    box-shadow: var(--shadow);
  }
  .mock-header {
    height: 28px;
    background: var(--header-bg);
    border-bottom: 1px solid var(--line);
    display: flex;
    align-items: center;
    padding: 0 8px;
    gap: 8px;
    flex: none;
  }
  .mock-brand {
    display: flex;
    align-items: center;
    gap: 5px;
    font-weight: 700;
    font-size: 11px;
    color: var(--text);
  }
  .mock-dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--accent);
  }
  .mock-search {
    flex: 1;
    max-width: 160px;
    height: 18px;
    background: var(--bg-input);
    border: 1px solid var(--line);
    border-radius: 4px;
    font-size: 9.5px;
    color: var(--text-faint);
    display: flex;
    align-items: center;
    padding: 0 6px;
  }
  .mock-actions {
    display: flex;
    gap: 4px;
    margin-left: auto;
  }
  .mock-icon-btn {
    width: 14px;
    height: 14px;
    border-radius: 3px;
    background: var(--bg-hover);
  }
  .mock-body {
    flex: 1;
    display: flex;
    min-height: 0;
    background: var(--bg);
    padding: 4px;
    gap: 4px;
  }
  .mock-sidebar {
    width: 80px;
    background: var(--sidebar-bg, var(--bg-panel));
    border: 1px solid var(--line);
    border-radius: 6px;
    padding: 4px;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .mock-nav-item {
    font-size: 10px;
    padding: 3px 5px;
    border-radius: 4px;
    color: var(--text-soft);
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .mock-nav-item.active {
    background: var(--bg-select);
    color: var(--text);
    font-weight: 600;
  }
  .mock-nav-bullet {
    width: 4px;
    height: 4px;
    border-radius: 50%;
    background: var(--accent);
  }
  .mock-editor {
    flex: 1;
    background: var(--editor-bg, var(--bg-panel));
    border: 1px solid var(--line);
    border-radius: 6px;
    padding: 8px 10px;
    display: flex;
    flex-direction: column;
    gap: 4px;
    overflow: hidden;
  }
  .mock-note-title {
    font-size: 13px;
    font-weight: 700;
    color: var(--text);
  }
  .mock-text {
    font-size: 10.5px;
    line-height: 1.3;
    color: var(--text-soft);
    margin: 0;
  }
  .mock-elements-row {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-top: 4px;
    font-size: 9.5px;
    flex-wrap: wrap;
  }
  .mock-wikilink {
    color: var(--accent);
    text-decoration: underline;
  }
  .mock-tag {
    background: var(--accent-wash);
    color: var(--accent);
    padding: 1px 4px;
    border-radius: 4px;
    font-weight: 600;
  }
  .mock-task {
    color: var(--text-muted);
    display: flex;
    align-items: center;
    gap: 3px;
  }
  .mock-check.done {
    color: var(--ok);
    font-weight: bold;
  }
  .mock-card {
    width: 85px;
    background: var(--bg-raised);
    border: 1px solid var(--line-strong);
    border-radius: 6px;
    padding: 6px;
    display: flex;
    flex-direction: column;
    gap: 3px;
  }
  .mock-card-head {
    font-size: 9.5px;
    font-weight: 700;
    color: var(--text);
    border-bottom: 1px solid var(--line);
    padding-bottom: 2px;
  }
  .mock-card-row {
    font-size: 9px;
    color: var(--text-soft);
  }
  .mock-card-row.muted {
    color: var(--text-muted);
  }
  .mock-ok-text {
    color: var(--ok);
    font-weight: 600;
  }
  .mock-statusbar {
    height: 18px;
    background: var(--statusbar-bg, var(--bg-raised));
    border-top: 1px solid var(--line);
    display: flex;
    align-items: center;
    padding: 0 8px;
    font-size: 9px;
    color: var(--text-muted);
    font-family: var(--font-mono);
  }
  .mock-spacer { flex: 1; }

  /* Presets */
  .presets { display: grid; grid-template-columns: repeat(auto-fill, minmax(110px, 1fr)); gap: 8px; flex: 1; min-width: min(260px, 100%); max-width: 760px; }
  .preset { display: grid; gap: 4px; padding: 6px; border: 1px solid var(--line-strong); border-radius: 9px; background: var(--bg-raised); cursor: pointer; text-align: left; }
  .preset.on { border-color: var(--accent); box-shadow: 0 0 0 1px var(--accent); }
  .sample { position: relative; display: block; height: 52px; border-radius: 6px; background: var(--pbg); overflow: hidden; border: 1px solid color-mix(in srgb, var(--ptext) 15%, transparent); }
  .sample .bar { position: absolute; left: 0; top: 0; bottom: 0; width: 26%; background: var(--ppanel); }
  .sample .line { position: absolute; left: 34%; top: 14px; width: 52%; height: 5px; border-radius: 3px; background: var(--ptext); opacity: .85; }
  .sample .line.short { top: 26px; width: 34%; opacity: .5; }
  .sample .dot { position: absolute; right: 8px; bottom: 8px; width: 12px; height: 12px; border-radius: 50%; background: var(--pacc); }
  .name { font-size: 12px; color: var(--text-soft); }

  /* Element Section */
  .elements-section {
    margin: 24px 0 16px;
    padding: 16px;
    background: color-mix(in srgb, var(--bg-raised) 70%, transparent);
    border: 1px solid var(--line);
    border-radius: 12px;
  }
  .elements-header {
    margin-bottom: 12px;
  }
  .elements-title-row {
    display: flex;
    align-items: center;
    gap: 10px;
    flex-wrap: wrap;
  }
  .elements-title-row .section-title {
    margin: 0;
  }
  .custom-badge {
    font-size: 11px;
    padding: 2px 7px;
    border-radius: 10px;
    background: var(--accent-wash);
    color: var(--accent-strong);
    font-weight: 600;
  }
  .auto-badge {
    font-size: 11px;
    padding: 2px 7px;
    border-radius: 10px;
    background: var(--bg-hover);
    color: var(--text-muted);
  }

  /* Category tabs */
  .category-tabs {
    display: flex;
    gap: 6px;
    margin-bottom: 14px;
    overflow-x: auto;
    padding-bottom: 4px;
  }
  .cat-tab {
    padding: 5px 10px;
    border-radius: 6px;
    border: 1px solid var(--line);
    background: var(--bg);
    color: var(--text-soft);
    font-size: 12.5px;
    cursor: pointer;
    display: flex;
    align-items: center;
    gap: 6px;
    white-space: nowrap;
  }
  .cat-tab.active {
    background: var(--accent-wash);
    color: var(--accent-strong);
    border-color: var(--accent);
    font-weight: 600;
  }
  .tab-count {
    font-size: 10px;
    padding: 1px 5px;
    border-radius: 8px;
    background: var(--accent);
    color: var(--accent-ink);
    font-weight: 700;
  }

  /* Elements Grid */
  .elements-grid {
    display: grid;
    gap: 8px;
  }
  .element-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 8px 10px;
    background: var(--bg);
    border: 1px solid var(--line);
    border-radius: 8px;
    gap: 12px;
  }
  .element-row.is-custom {
    border-color: color-mix(in srgb, var(--accent) 40%, var(--line));
    background: color-mix(in srgb, var(--accent-wash) 20%, var(--bg));
  }
  .el-info {
    display: flex;
    flex-direction: column;
    gap: 1px;
    min-width: 0;
  }
  .el-label {
    font-size: 13.5px;
    font-weight: 600;
    color: var(--text);
  }
  .el-desc {
    font-size: 11.5px;
    color: var(--text-muted);
  }
  .el-controls {
    display: flex;
    align-items: center;
    gap: 8px;
    flex: none;
  }
  .status-pill.auto {
    font-size: 11px;
    padding: 3px 8px;
    border-radius: 6px;
    background: var(--bg-hover);
    color: var(--text-faint);
  }

  input[type='color'] { width: 34px; height: 28px; padding: 0; border: 1px solid var(--line-strong); border-radius: 6px; background: none; cursor: pointer; }
  input[type='color']:disabled { opacity: .4; cursor: default; }
  .hex { width: 95px; font-family: var(--font-mono); font-size: 12px; }
  .font { width: 220px; max-width: 100%; }
  .field input[type='range'] { min-width: 0; max-width: 100%; }
  .preview { font-size: 15px; color: var(--text-soft); }
  .swatches { display: flex; gap: 6px; }
  .swatch { width: 22px; height: 22px; border-radius: 50%; background: var(--c); border: 2px solid transparent; cursor: pointer; padding: 0; }
  .swatch.on { border-color: var(--text); }
  .seg { display: inline-flex; border: 1px solid var(--line-strong); border-radius: 7px; overflow: hidden; }
  .seg button { border: 0; background: none; color: var(--text-muted); padding: 5px 10px; font: 13px var(--font-body); cursor: pointer; }
  .seg button + button { border-left: 1px solid var(--line-strong); }
  .seg button.on { background: var(--accent-wash); color: var(--accent-strong); }
  .small { font-size: 12.5px; }
  .small-btn { font-size: 12px; padding: 4px 8px; height: 28px; }
  .indent { margin: -4px 0 6px 140px; }
  .mono { font-family: var(--font-mono); font-size: 12.5px; }
  .row { display: flex; flex-wrap: wrap; gap: 8px; margin: 14px 0 6px; }
  .actions-row { margin-top: 20px; border-top: 1px solid var(--line); padding-top: 16px; }
  .danger-btn:hover { color: var(--danger); }
</style>
