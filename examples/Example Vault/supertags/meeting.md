---
id: 01JQEXAMP0E000000000000002
title: meeting
type: supertag
color: "#78b6ff"
template: "[[01JQEXAMP0E000000000000010|Meeting notes]]"
auto_apply_template: true
fields:
  - name: When
    type: date
    required: true
  - name: Status
    type: select
    options: [Planned, Held, Cancelled]
  - name: Attendees
    type: nodes
schema: 1
---