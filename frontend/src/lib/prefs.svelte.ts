/** Per-device writing preferences (kept in this browser; vault settings live in settings.json). */
class Prefs {
  typewriter = $state(false);
  editorSize = $state(17);
  editorWidth = $state(760);
  showWordCount = $state(true);
  writingTarget = $state<Record<string, number>>({});

  constructor() {
    try {
      const saved = JSON.parse(localStorage.getItem('quantos.prefs') ?? '{}');
      Object.assign(this, saved);
    } catch { /* ignore */ }
    $effect.root(() => {
      $effect(() => {
        const data = { typewriter: this.typewriter, editorSize: this.editorSize, editorWidth: this.editorWidth, showWordCount: this.showWordCount, writingTarget: this.writingTarget };
        document.documentElement.style.setProperty('--editor-size', this.editorSize + 'px');
        document.documentElement.style.setProperty('--editor-width', this.editorWidth + 'px');
        try { localStorage.setItem('quantos.prefs', JSON.stringify(data)); } catch { /* ignore */ }
      });
    });
  }
}

export const prefs = new Prefs();
