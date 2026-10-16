import { api } from './api';

export interface TemplateInfo { id: string; title: string; path: string; tags: string[]; description: string | null }

class Templates {
  list = $state<TemplateInfo[]>([]);
  async load() { try { this.list = await api.get<TemplateInfo[]>('templates'); } catch { this.list = []; } }
}

export const templatesStore = new Templates();
