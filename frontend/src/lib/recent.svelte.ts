/** Recent-document navigation (BUILD-PLAN 8A.16): most recently opened nodes, per device. */
class Recent {
  ids = $state<string[]>([]);
  constructor() {
    try { this.ids = JSON.parse(localStorage.getItem('quantos.recent') ?? '[]'); } catch { this.ids = []; }
  }
  touch(id: string) {
    this.ids = [id, ...this.ids.filter((x) => x !== id)].slice(0, 30);
    try { localStorage.setItem('quantos.recent', JSON.stringify(this.ids)); } catch { /* ignore */ }
  }
}
export const recent = new Recent();
