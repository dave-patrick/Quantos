---
id: 01JQEXAMP0E000000000000020
title: Welcome to Quantos
type: note
workspace: default
created: 2026-10-15T09:00:00+00:00
modified: 2026-10-15T09:00:00+00:00
schema: 1
---
This is an example vault. Every note is a Markdown file you can open in any editor.

Start with the [[01JQEXAMP0E000000000000030|Apollo launch]] project, which has a meeting with
[[01JQEXAMP0E000000000000040|Sam Rivera]].

> [!tip]- Things to try (click the arrow)
> - Press Ctrl+P and type "apollo".
> - Type `[[` to link a note.
> - Type `- [ ] something` to create a task.
> - Open Tasks, My Day or the Graph from the left sidebar.

## Active projects

```query
tags: [project]
where: status = Active
columns: [title, status, owner, due]
```

:::columns
**Notes** link to each other and show backlinks in the right sidebar.
+++
**Tasks** written in notes stay in sync with the task list.
:::

Footnotes preview on hover.[^1]

[^1]: Like this one.