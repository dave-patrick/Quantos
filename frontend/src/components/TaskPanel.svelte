<script lang="ts">
  import type { Doc } from '../lib/docs.svelte';
  let { doc }: { doc: Doc } = $props();
  void doc;
</script>
