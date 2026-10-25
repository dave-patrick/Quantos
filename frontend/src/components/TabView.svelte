<script lang="ts">
  import type { Tab } from '../lib/layout.svelte';
  import NodeView from './views/NodeView.svelte';
  import DailyStream from './views/DailyStream.svelte';
  import SearchView from './views/SearchView.svelte';
  import TrashView from './views/TrashView.svelte';
  import HistoryView from './views/HistoryView.svelte';
  import DiagnosticsView from './views/DiagnosticsView.svelte';
  import SettingsView from './views/SettingsView.svelte';
  import AskView from './views/AskView.svelte';
  import ReplaceView from './views/ReplaceView.svelte';
  import FocusView from './views/FocusView.svelte';
  import HabitsView from './views/HabitsView.svelte';
  import SprintsView from './views/SprintsView.svelte';
  import GoalsView from './views/GoalsView.svelte';
  import GanttView from './views/GanttView.svelte';
  import MindmapView from './views/MindmapView.svelte';
  import MapView from './views/MapView.svelte';
  import NotesView from './views/NotesView.svelte';
  import PhoneNotes from './views/PhoneNotes.svelte';
  import { isCompactAndroid } from '../lib/platform';
  import HelpView from './views/HelpView.svelte';
  import AboutView from './views/AboutView.svelte';
  import TasksView from './views/TasksView.svelte';
  import MyDayView from './views/MyDayView.svelte';
  import TimelineView from './views/TimelineView.svelte';
  import ReviewView from './views/ReviewView.svelte';
  import QueryView from './views/QueryView.svelte';
  import DictionaryView from './views/DictionaryView.svelte';
  import AttachmentsView from './views/AttachmentsView.svelte';
  import TemplatesView from './views/TemplatesView.svelte';
  import SupertagsView from './views/SupertagsView.svelte';
  import GraphView from './views/GraphView.svelte';
  import NotificationsView from './views/NotificationsView.svelte';
  import PdfView from './views/PdfView.svelte';

  let { tab, paneIndex, today }: { tab: Tab; paneIndex: number; today: string } = $props();
</script>

{#if tab.kind === 'node' && tab.nodeId}
  <NodeView {tab} id={tab.nodeId} {paneIndex} />
{:else if tab.kind === 'daily'}
  <DailyStream {tab} {today} />
{:else if tab.kind === 'ask'}
  <AskView />
{:else if tab.kind === 'replace'}
  <ReplaceView />
{:else if tab.kind === 'focus'}
  <FocusView />
{:else if tab.kind === 'habits'}
  <HabitsView />
{:else if tab.kind === 'sprints'}
  <SprintsView />
{:else if tab.kind === 'goals'}
  <GoalsView />
{:else if tab.kind === 'gantt'}
  <GanttView />
{:else if tab.kind === 'map'}
  <MapView />
{:else if tab.kind === 'notes'}
  {#if isCompactAndroid() && tab.arg !== 'advanced'}<PhoneNotes />{:else}<NotesView />{/if}
{:else if tab.kind === 'help'}
  <HelpView {tab} />
{:else if tab.kind === 'about'}
  <AboutView />
{:else if tab.kind === 'mindmap' && tab.nodeId}
  {#key tab.nodeId}<MindmapView id={tab.nodeId} />{/key}
{:else if tab.kind === 'search'}
  <SearchView {tab} />
{:else if tab.kind === 'trash'}
  <TrashView />
{:else if tab.kind === 'history' && tab.nodeId}
  <HistoryView id={tab.nodeId} />
{:else if tab.kind === 'diagnostics'}
  <DiagnosticsView />
{:else if tab.kind === 'settings'}
  <SettingsView {tab} />
{:else if tab.kind === 'tasks'}
  <TasksView {tab} />
{:else if tab.kind === 'myday'}
  <MyDayView {today} />
{:else if tab.kind === 'timeline'}
  <TimelineView {today} />
{:else if tab.kind === 'review'}
  <ReviewView />
{:else if tab.kind === 'view' || tab.kind === 'query'}
  <QueryView {tab} />
{:else if tab.kind === 'dictionary'}
  <DictionaryView />
{:else if tab.kind === 'attachments'}
  <AttachmentsView />
{:else if tab.kind === 'templates'}
  <TemplatesView />
{:else if tab.kind === 'supertags'}
  <SupertagsView {tab} />
{:else if tab.kind === 'graph'}
  <GraphView {tab} />
{:else if tab.kind === 'notifications'}
  <NotificationsView />
{:else if tab.kind === 'pdf'}
  <PdfView {tab} />
{:else}
  <div class="empty-state"><h3>Nothing here</h3></div>
{/if}
