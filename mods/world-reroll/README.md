<p align="center"><img src="src/main/resources/assets/worldreroll/icon.png" width="96" alt="World Reroll icon"></p>

# World Reroll

[English](README.md) · [简体中文](README.zh-CN.md)

Try another singleplayer start without deleting a save and configuring a new world by hand. World Reroll keeps your world settings, chooses a fresh random seed, and can place your new start near a village.

**Minecraft 26.3 · Fabric / Quilt / Forge / NeoForge · Java 25 · Singleplayer**. Fabric 26.2 / 26.1.2 are also bundled; see the [exact compatibility matrix](../COMPATIBILITY.md).

> **Rerolling permanently deletes the previous world's terrain, buildings, inventory and progress after the replacement is ready. Use it only for worlds you intend to discard.**

## Choose your next start

Settings, confirmation, village preparation and retry use World Reroll's own palette and Minecraft font; native world-loading and saving screens retain their callbacks. No Rodel UI classes, reflection or theme are used. It follows Minecraft GUI scaling, keeps footer actions accessible and scrolls overflowing text without replacing native world callbacks.

- **Random spawn** — explore the new seed from its natural spawn point.
- **Near a village** — locate an existing village and move both the player and world spawn to nearby solid ground with clear space above it. This moves the spawn after generation; it does not filter seeds for a natural village spawn.
- **Settings stay with you** — keep mode, difficulty and difficulty lock, game rules, generator, structure and bonus-chest options, enabled features, and world datapacks.
- **Readable save names** — new folders use names such as `HelloWorld-reroll-1`. Occupied names are skipped; existing saves are not renamed.

Village mode is unavailable when structure generation is disabled. Searching has a bounded range and may take time. If no village or suitable ground is found, the original world is retained. Your last selection is remembered during the current game session.

Preparation is cancellable and keeps the server running, with at most two native chunk requests in flight. Real structure starts determine the village; the safe position and its 3×3 FULL-chunk neighborhood are prepared before teleporting. Paths and thin/thick snow remain valid standing surfaces, and the preparing player is protected without changing saved invulnerability settings.

When Bonus Chest is enabled, its unopened vanilla loot table and seed move to an accessible chest near the final village spawn. Ordinary village chests are not moved. If vanilla could not place its original bonus chest, one is generated at the final spawn instead.

## Start a new world near a village

In **Create New World → World**, enable **Spawn near a village**, alongside **Generate Structures** and **Bonus Chest**. The option starts off for each new creation screen and requires structure generation. Your chosen seed and other creation settings stay unchanged; only the initial player/world spawn and enabled bonus chest are relocated.

![Village-spawn option in the English world-creation screen](docs/screenshots/create-world-en.png)

This initial-creation flow never deletes another save. Failed village placement shows a notice and keeps the newly created world so you can continue playing. Selecting this option does not remember a deletion request or change an existing world's spawn on later loads.

![A village in a generated replacement world](docs/screenshots/village-overview.png)

*A real village in a replacement world, shown from a spectator overview. This is not the initial player viewpoint.*

## Install

| Requirement | Supported version |
| --- | --- |
| Minecraft Java Edition | 26.3 |
| Mod loader | A verified Fabric, Quilt, Forge or NeoForge target from the compatibility matrix |
| Dependency | Fabric/Quilt: matching Fabric API; native Forge/NeoForge: no Fabric API |
| Java | 25 or newer |
| Environment | Singleplayer client; unavailable on multiplayer and open-to-LAN worlds |

1. Prepare a supported Minecraft release and Fabric in your launcher instance.
2. In the launcher, open **Content → Mods → Built-in mods**, select World Reroll and choose **Install selected**. Required Fabric API is included automatically.
3. Launch that instance. The launcher ships the mod directly; installation does not use community distribution.

Lithergy is not required. Use the exact Minecraft target selected by the launcher. See the [compatibility matrix](../COMPATIBILITY.md); matching mod versions do not make different game targets interchangeable.

## Use

1. Enter a singleplayer world and press **Esc**.
2. Click **Reroll**, next to **Save and Quit to Title**.
3. Choose a spawn preference in the panel, then click **Continue**.
4. Read the deletion notice and confirm **Reroll world**, or cancel to keep playing.

The original world is saved and closed normally. A separate replacement folder is created with a new seed and copied world datapacks. Deletion begins only after the replacement is playable for 20 consecutive client ticks. In village mode, the new position and all nine nearby chunks must reach the client before that interval starts. Cancelling or retrying releases only the attempt's own temporary tickets and retains the pinned original source.

## What stays, and what resets?

| Kept | Reset |
| --- | --- |
| World display name, mode, difficulty and lock | Terrain and buildings |
| All saved game rules, including changes made in-game | Inventory, player state and progression |
| Generator, structure and bonus-chest options | Seed |
| Enabled features and world datapacks | Arbitrary third-party per-save state |

The new world starts fresh even when rules such as Keep Inventory are enabled. Compatibility with mods that replace world loading or depend on custom per-save data requires separate validation.

## Failure handling

- Cancelling before reroll, creation failure, or failed village placement retains the source world.
- Choose **Try again** on the failure screen to use another seed with the original world's settings and spawn preference, or choose **Back** / press Escape to leave. Only a playable retry can delete the original source; failed replacements remain available.
- Restarting Minecraft never resumes a pending deletion.
- Only the pinned source save is a deletion target; a unique marker and filesystem metadata verify its identity. Linked or redirected directories are rejected.
- Cleanup errors are reported in-game and logged. Once deletion has begun, an error can leave partial cleanup; the replacement remains usable.
- Failed replacements remain in the saves directory for diagnosis. A crash can leave harmless `.worldreroll-*` marker files; these never schedule deletion by themselves.

Report problems through [GitHub Issues](https://github.com/Richasy/WorldReroll/issues), including the game/mod versions, reproduction steps and relevant log excerpt.

## Development

This independent Gradle project lives at `mods/world-reroll`, outside the .NET solution. Fabric/Quilt packages require matching Fabric API; native Forge/NeoForge packages require their own loader and game. The launcher, Rodel UI and development directories remain optional at runtime. See [validation notes](docs/VALIDATION.md).

The authoritative source is this launcher-owned module, not the older standalone repository. Versioning remains shared through `mods/gradle.properties`. Ticket registration, server ticks/shutdown and temporary player protection use common native Mixins, so the server pipeline does not introduce a Fabric-only initializer into Forge/NeoForge packages.

From the launcher repository root, enter this module and use JDK 25 with its checked-in wrapper:

```powershell
cd .\mods\world-reroll
.\gradlew.bat build --no-daemon
.\gradlew.bat runClientGameTest --no-daemon
```

Ten unit tests cover attempt transitions, save identity/naming and datapacks. Native client tests exercise compact presentation, cancellation, preserved settings and datapacks, delayed deletion, LAN gating, village placement and failure preservation. They use disposable saves under `build/run/clientGameTest`; manual development uses `run/client`. Never point deletion tests at personal saves. See [validation notes](docs/VALIDATION.md).

## License and credits

[MIT](LICENSE). Inspired by the workflow of [Start Reroll](https://github.com/euruseve/StartReroll), with an independent implementation using Minecraft 26.3 APIs. Code, translations and documentation were developed with AI assistance; the icon was generated through Azure ImageGen. Documentation screenshots are captured in Minecraft, not generated images.

## Appearance settings

Open this mod's configuration from the mod list or the reroll options header. Done saves Light/Dark to `config/worldreroll-client.properties`; Cancel retains the previous choice. Fabric/Quilt use optional Mod Menu; Forge/NeoForge use their native mod list. The palette is independent of the launcher and other mods.
