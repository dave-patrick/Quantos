<script lang="ts" module>
  // Stroke icons, 24px grid.
  const paths: Record<string, string> = {
    menu: 'M4 6h16M4 12h16M4 18h16',
    sparkles: 'M12 3l1.8 4.6L18 9.5l-4.2 1.9L12 16l-1.8-4.6L6 9.5l4.2-1.9zM19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8z',
    'panel-left': 'M3 4h18v16H3zM9 4v16',
    'panel-right': 'M3 4h18v16H3zM15 4v16',
    search: 'M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14zM20 20l-4-4',
    plus: 'M12 5v14M5 12h14',
    x: 'M6 6l12 12M18 6L6 18',
    calendar: 'M4 6h16v14H4zM4 10h16M8 3v4M16 3v4',
    sun: 'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4',
    check: 'M5 12l5 5L20 7',
    'check-square': 'M4 4h16v16H4zM8 12l3 3 5-6',
    file: 'M6 3h8l4 4v14H6zM14 3v4h4',
    play: 'M7 4l13 8-13 8z',
    'file-text': 'M6 3h8l4 4v14H6zM14 3v4h4M9 12h6M9 16h6',
    folder: 'M3 6h6l2 2h10v11H3z',
    'folder-open': 'M3 6h6l2 2h8v3M3 6v13h15l3-8H6l-3 8',
    trash: 'M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3',
    archive: 'M3 4h18v4H3zM5 8v12h14V8M10 12h4',
    clock: 'M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16zM12 8v4l3 2',
    history: 'M3 12a9 9 0 1 0 3-6.7L3 8M3 3v5h5M12 7v5l3 2',
    link: 'M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1',
    tag: 'M3 12V3h9l9 9-9 9zM7.5 7.5h.01',
    hash: 'M5 9h14M5 15h14M10 3L8 21M16 3l-2 18',
    bell: 'M6 8a6 6 0 1 1 12 0c0 7 3 9 3 9H3s3-2 3-9M10 21h4',
    settings: 'M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1A1.7 1.7 0 0 0 9 19.4a1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1A1.7 1.7 0 0 0 4.6 9a1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z',
    activity: 'M3 12h4l3-8 4 16 3-8h4',
    split: 'M3 4h18v16H3zM12 4v16',
    'split-h': 'M3 4h18v16H3zM3 12h18',
    'arrow-left': 'M19 12H5M11 6l-6 6 6 6',
    'arrow-right': 'M5 12h14M13 6l6 6-6 6',
    'arrow-up': 'M12 19V5M6 11l6-6 6 6',
    'arrow-down': 'M12 5v14M6 13l6 6 6-6',
    'chevron-right': 'M9 6l6 6-6 6',
    'chevron-down': 'M6 9l6 6 6-6',
    pin: 'M9 4h6l-1 6 4 3H6l4-3zM12 13v8',
    more: 'M12 6h.01M12 12h.01M12 18h.01',
    edit: 'M4 20h4L19 9l-4-4L4 16zM14 6l4 4',
    code: 'M8 7l-5 5 5 5M16 7l5 5-5 5',
    eye: 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zM12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z',
    star: 'M12 3l2.8 5.7 6.2.9-4.5 4.4 1 6.2L12 17.3 6.5 20.2l1-6.2L3 9.6l6.2-.9z',
    layers: 'M12 3l9 5-9 5-9-5zM3 13l9 5 9-5',
    grid: 'M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z',
    list: 'M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01',
    kanban: 'M4 4h4v16H4zM10 4h4v10h-4zM16 4h4v13h-4z',
    table: 'M3 5h18v14H3zM3 10h18M3 15h18M9 5v14',
    graph: 'M6 6a2 2 0 1 0 0 .01M18 6a2 2 0 1 0 0 .01M12 18a2 2 0 1 0 0 .01M7.5 7.5l3.5 9M16.5 7.5l-3.5 9M8 6h8',
    paperclip: 'M20 11l-8.5 8.5a5 5 0 0 1-7-7L13 4a3.5 3.5 0 0 1 5 5l-8.5 8.5a2 2 0 0 1-3-3L14 7',
    book: 'M4 4h6a3 3 0 0 1 3 3v13a2 2 0 0 0-2-2H4zM20 4h-6a3 3 0 0 0-3 3v13a2 2 0 0 1 2-2h7z',
    terminal: 'M4 5h16v14H4zM7 9l3 3-3 3M12 15h5',
    zap: 'M13 2L4 14h7l-1 8 9-12h-7z',
    inbox: 'M3 13h5l2 3h4l2-3h5M5 5h14l2 8v6H3v-6z',
    alert: 'M12 3l10 18H2zM12 10v5M12 18h.01',
    info: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 11v6M12 7h.01',
    refresh: 'M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7',
    download: 'M12 3v12M6 11l6 6 6-6M4 21h16',
    upload: 'M12 21V9M6 13l6-6 6 6M4 3h16',
    copy: 'M8 8h12v12H8zM4 16V4h12',
    user: 'M12 4a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM4 21a8 8 0 0 1 16 0',
    briefcase: 'M3 7h18v13H3zM8 7V4h8v3M3 13h18',
    filter: 'M3 4h18l-7 9v6l-4 2v-8z',
    sort: 'M7 4v16M3 16l4 4 4-4M17 20V4M13 8l4-4 4 4',
    target: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10zM12 11h.01',
    timer: 'M12 6a8 8 0 1 0 0 16 8 8 0 0 0 0-16zM12 10v4M9 2h6',
    repeat: 'M17 2l4 4-4 4M3 11V9a3 3 0 0 1 3-3h15M7 22l-4-4 4-4M21 13v2a3 3 0 0 1-3 3H3',
    flag: 'M5 21V4M5 4h11l-2 4 2 4H5',
    moon: 'M21 13a9 9 0 1 1-10-10 7 7 0 0 0 10 10z',
    command: 'M9 6a3 3 0 1 0-3 3h12a3 3 0 1 0-3-3v12a3 3 0 1 0 3-3H6a3 3 0 1 0 3 3z',
    puzzle: 'M10 4a2 2 0 1 1 4 0v2h4v4h-2a2 2 0 1 0 0 4h2v4h-4v-2a2 2 0 1 0-4 0v2H6v-4H4a2 2 0 1 1 0-4h2V6h4z',
    shield: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z',
    database: 'M12 3c4.4 0 8 1.3 8 3s-3.6 3-8 3-8-1.3-8-3 3.6-3 8-3zM4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3',
    feather: 'M20 4a6 6 0 0 0-8.5 0L5 10.5V19h8.5L20 12.5A6 6 0 0 0 20 4zM16 8L2 22M17.5 15H9',
    send: 'M22 2L11 13M22 2l-7 20-4-9-9-4z',
    lock: 'M5 11h14v10H5zM8 11V7a4 4 0 0 1 8 0v4',
    replace: 'M4 7h11M12 4l3 3-3 3M20 17H9M12 14l-3 3 3 3'
  };
</script>

<script lang="ts">
  let { name, size = 16, stroke = 1.8, class: cls = '' }: { name: string; size?: number; stroke?: number; class?: string } = $props();
</script>

<svg class="icon {cls}" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width={stroke} stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
  <path d={paths[name] ?? paths.file} />
</svg>

<style>
  .icon { flex: none; display: block; }
</style>
