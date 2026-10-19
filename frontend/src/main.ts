import './styles/tokens.css';
import './styles/base.css';
import { mount } from 'svelte';
import App from './App.svelte';
import { watchRichContent } from './lib/diagrams';

mount(App, { target: document.getElementById('app')! });
watchRichContent();
