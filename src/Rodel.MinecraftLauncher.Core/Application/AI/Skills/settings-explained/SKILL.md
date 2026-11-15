---
name: settings-explained
description: Read effective launcher settings, explain inheritance, and prepare game bridge access for in-game observations or actions.
---
Read config_get for the explicit instance or global scope requested by the user.
Explain the observed effective value and its source, not merely a guessed default.
Distinguish heap configuration from actual process memory consumption.
Compatibility recommendations are advice, not a reason to reject a custom runnable setup.
Retain unknown fields and manual Java/JVM choices. In read-only mode explain proposals without saving.
With host-granted operation permission, prefer config_patch for changes and override removal.
Change only the requested fields, distinguish explicit null from removed inheritance, and read back
effective values and sources. Do not turn a proposal, unknown effect, or successful transport into
a claim that the desired current value was verified.

For game bridge access, read config_get for the explicit instance and inspect gameBridgeEnabled's effective value/source; an absent override defaults to enabled, while an explicit false remains disabled. Check the actual session with game_bridge_show as well: saved configuration controls future launches and does not prove a running bridge is connected.
If disabled, explain the global Settings > Game launch option and the instance setting (inherit global, disabled or enabled). Prefer an instance-only config_patch with changes {"gameBridgeEnabled":true} when enabling for the requested instance is within the user's task and the host's granted operation permission. In read-only mode, explain the required change; in safe mode, use the offered write tool so the host can request exact permission. Do not grant yourself permission or silently change the global default.
Read back the effective setting and source after a successful write. For a running game without the agent, say that enabling applies to the next launch; preserve the current game and do not stop/restart it without task authorization. Before reading or acting, inspect the new session's actual bridge status and a fresh game_snapshot. A disconnected or unsupported bridge may need diagnosis rather than another enable write.
To restore inheritance, remove the instance override with config_patch removeKeys, not a false or null value. Bridge enablement does not authorize game edits or server writes: remote servers, LAN guests and localhost dedicated servers remain read-only even for OP.
