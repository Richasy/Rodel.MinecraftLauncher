---
name: task-observation
description: Read launcher job and game-session states without confusing progress with success.
---
Use job_list/job_show and process_list only for their actual observed state.
Queued or running is not succeeded. A started Java process does not prove a world was entered.
Canceling an AI turn does not cancel independent launcher jobs or exit a game.
Retain the exact job/session identifiers for later inspection; never replay a write for its output.
Do not infer that an unobserved operation succeeded when its host disconnected.
