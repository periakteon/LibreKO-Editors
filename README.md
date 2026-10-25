# LibreKO Editors

A standalone Windows desktop editor for LibreKO, built with Godot C# and a
.NET data layer. Browse game records, inspect their visuals and save reviewed
changes to the database or explicitly selected source files.

[Download the latest release](https://github.com/periakteon/LibreKO-Editors/releases/latest)
· [Contributing](CONTRIBUTING.md) · [User guide](docs/help/overview.md) · [License](LICENSE)

## Editors

| Workspace | What you can manage |
| --- | --- |
| Content | Items, player characters, visual definitions, equipment/inventory and accounts, including GM role, Knight Cash and premium |
| Events | Temple activities, Forgotten Temple waves, Collection Race, Lottery, Castle Siege, map events, objects and NPC triggers |
| Map & NPC | Zones, NPCs/monsters, spawns and patrols, drops, item pools, summons, exchanges, merchant stock and supported map/quest/client sources |

- Shared Home, editor switching, browser-style Back/Forward, loading indicators and named ID pickers.
- Item icons, models and effects; character equipment and draggable inventory with detailed hover tooltips.
- Interactive 3D maps, spawn focus/placement and full-screen editing.
- Shop and loot grids beside the NPC model, with links to item origins.
- Explicit saves, stale-record checks, offline-player guards, undo/redo, database-scoped history and interrupted-operation reconciliation.
- Searchable Help/F1 with production activation, verification and rollback steps.

## Install

1. Download the Windows x64 ZIP from [Releases](https://github.com/periakteon/LibreKO-Editors/releases).
2. Extract the entire archive into a writable folder. Keep the EXE, PCK, `data`, `data_LibreKO.Editors.Godot_windows_x86_64` and `help` together.
3. Run `LibreKO.Editors.exe`. The release includes its .NET runtimes; ordinary database editing does not require an SDK or the Godot editor.
4. In Settings, select your own installed LibreKO client assets and workspace. Proprietary game assets and personal profiles are not distributed here.
5. Use **Docker** to select a running database container, or **Connection…** to configure MySQL/MariaDB. Start with staging and run Compatibility.

Credentials remain in memory. `LIBREKO_EDITOR_DB` can supply a connection for local automation; keep it out of source control, screenshots and issue reports. Docker discovery can use published ports or a temporary local stream without reconfiguring the container.

## Saving and production updates

Content/Events database operations require **Save to database**. Source authoring uses its own review/apply workflow. Installed packed assets are read-only reference content; changing a database ID does not generate icons or models.

The running server caches data. In the supported LibreKO revision, startup may reseed JSON-backed tables when fingerprints **or row counts** differ. The `+reloadgamedata`, `+reloaddrops` and `+reseed` aliases force reseeding and can overwrite database edits. Read the applicable guide before restarting/reloading:

- [Database configuration](docs/help/database.md)
- [Player characters and accounts](docs/help/players.md)
- [Server seed files](docs/help/seeds.md)
- [Shops and services](docs/help/shops.md)
- [Maps](docs/help/maps.md), [quests](docs/help/quests.md) and [client releases](docs/help/client.md)
- [Coordinated release and rollback](docs/help/release.md)

Merchant stock also needs matching client catalogue entries. Client/source changes require a separate build and production rollout; the editor does not automatically deploy, restart or reseed a game service.

## Compatibility and limitations

This release targets the source revision in [dependencies.json](dependencies.json). It reflects supported mapped fields, reports schema/content differences and keeps unknown stored values visible. Arbitrary forks can change enum meanings, binary layouts and runtime behavior; structural checks cannot certify those semantics. Unsupported inventory layouts disable inventory editing while preserving their original bytes. Missing client assets leave database forms usable.

Events cover 14 supported tables; Map & NPC uses an explicit whitelist. Newly introduced upstream tables are not automatically exposed. Calendar schedules support Daily, Weekly, Once and Monthly; dates use `yyyy-MM-dd`. Calendar overlap warnings still use legacy weekday/time comparisons and can be inaccurate; they are advisory and do not block saves.

## Build from source

Use Windows x64, PowerShell 7.2+, .NET 10 SDK and Godot **4.7.2 Mono**. Install matching Mono export templates to create Windows packages. The game dependency and editor must be siblings with these names:

```text
workspace/
  LibreKO/       # pinned game source
  editors/       # this repository
```

```powershell
git clone https://github.com/periakteon/LibreKO.git LibreKO
git -C LibreKO checkout e5f0e4200fd42f69934f55557f92f2ee2ccfafde
git clone https://github.com/periakteon/LibreKO-Editors.git editors
Set-Location editors

$env:LIBREKO_GODOT = 'C:/Tools/Godot/Godot_v4.7.2-stable_mono_win64_console.exe'
./scripts/build.ps1
./scripts/run.ps1
```

`LIBREKO_GODOT`, `-GodotEditor` or a Godot executable on PATH selects the engine. The non-Mono build cannot run this C# application. Use your own asset paths in Settings; [profiles/example.json](profiles/example.json) shows the base configuration. Full build, test and packaging instructions are in [CONTRIBUTING.md](CONTRIBUTING.md).

## Project layout

| Directory | Purpose |
| --- | --- |
| `src/LibreKO.Editors.Contracts` | Serializable records and helper protocol contracts |
| `src/LibreKO.Editors.Core` | Drafts, validation, history, content and source handling |
| `src/LibreKO.Editors.DataHost` | Private helper process for database operations and Docker access |
| `src/LibreKO.Editors.Godot` | Shared shell, editors, previews and native UI checks |
| `tests` | Core and isolated database regression suites |
| `docs/help` | Canonical production/user guides, also embedded in the app |

## License and attribution

Editor source is distributed under [GNU AGPL v3](LICENSE). It references and adapts [LibreKO](https://github.com/ZeusAFK/LibreKO); the exact tested fork/commit is recorded in `dependencies.json`. See [third-party notices](THIRD_PARTY_NOTICES.md) and [upstream server notices](LibreKO-SERVER-NOTICES.md).

Godot, .NET and other dependencies retain their own licenses. Knight Online is a trademark of its respective owners. This independent project is not affiliated with NTTGame or MGame. Game artwork, packs, database dumps, credentials and local development history are excluded from this public repository.
