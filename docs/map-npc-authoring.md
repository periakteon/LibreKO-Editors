# Map & NPC authoring

Open the packaged `LibreKO.Editors.exe`, choose **Map & NPC Editor** on Home, and connect through **Docker** or **Connection…**. The header switches to Content or Events with the same connection. Database changes remain drafts until saved; history is scoped to the original database. Online-player restrictions in Content still apply.

Use **Help / F1** for step-by-step production instructions. Source review and client build also have **Production steps…** links. Server [database](help/database.md), [seed](help/seeds.md), [shop](help/shops.md), [map](help/maps.md), [quest](help/quests.md) and [client](help/client.md) workflows describe the actual destinations, activation, validation and rollback separately. A database row-count change can trigger startup reseeding; read the database guide before restarting after additions or deletions.

## Navigation and visual stock editing

Use the header arrows or Alt+Left/Right to retrace records, source documents and related links across all workspaces. Camera, selection, filters and tabs are restored without restoring old database values. Saving, discarding or cancelling a dirty draft happens before leaving it; a cancelled or failed navigation does not consume its history entry.

Select a zone, then click a related spawn to focus its exact position without leaving the zone. NPC and monster placement rows include zone names, coordinates, quantity, respawn and patrol context. **Show on map** opens the selected spawn, including placements in another zone. The Content item's **Origins** tab lists typed sellers, direct drops and ordered-pool drops, with links back to their definitions. Missing shop sources are reported separately from an empty result.

**Orbit camera** returns to orbit mode; right-drag rotates, middle-drag pans and the wheel zooms. **Top view** looks down on the current focus. **Focus selected** frames the selected placement. **Fly camera** enables W/A/S/D and Q/E while the viewport has focus. **Grid snap** quantizes spatial edits; **Ground snap** places edited positions on available terrain. Each control has a tooltip and **Controls…** provides the full interaction guide. **Expand map** hides surrounding panels; **Full screen** or F11 adds native full screen. Escape exits full screen and expanded editing while retaining the draft and camera.

For an NPC, monster, drop group or item pool, select the inspector's **Shop & loot** tab. Keep the model visible while using **Loot slots**, **Merchant shop** or **Ordered pool**. Search for an item by name/ID, drag its icon to a slot, or select a slot and use **Choose item…**. Hover for details. Dragging occupied loot slots swaps both item and probability as one undo step. Pool order and duplicate members remain meaningful; moving a merchant slot retains unknown fields in its complete source row. Shop lines and slots are paged, and clear/reorder operations remain drafts.

**Shared users…** resolves typed NPC/monster owners for shops and drop groups. For pools it follows every referencing drop definition, pages the actual consumers, and retains unassigned drop definitions as navigable rows. Review these owners before saving a shared resource. Low positive item IDs in merchant stock and pool membership remain direct item IDs; only loot-slot values in the server's pool-reference range are interpreted as pools.

Database loot/pool edits use database review and its transaction/history. Merchant stock uses the registered `ItemSells.json` document and source review/history, shared with the structured source view. Merely opening a board does not add read-only definitions to the editable database draft. A delayed response for an older pool cannot replace a newly selected view. Global and panel activity indicators cover ongoing loads.

## Source setup

Use **Sources…** to select a named source profile. Server data, SMD maps, quest scripts, the raw Godot client project, the retail asset pipeline/input and a separate build-output directory have distinct roots. Optional missing roots do not disable unrelated editors. Source roots retain their physical identity; linked/escaping/replaced paths are refused. A source profile is independent of the installed packed build selected in Settings.

The source library provides supported files with structured and raw views. Arbitrary code is not a generic executable editor. Zone policies implemented in server Startup/World C# still require a source-code change and server compilation outside these forms. Named source/database references show their origin; custom runtime meanings cannot be inferred merely from an integer or a same-sized blob.

**Close document** and **Return to database** leave the source page through the existing dirty-document guard. Review/discard/cancel keeps unsaved decisions explicit. A captured preview is a separate window: Escape, its Close button and the window close control stop that preview without closing the editor.

## Save and recovery

File review shows target paths, source identity, before/after evidence, validation and independent outcomes. Existing target bytes and consumed dependencies are checked again before replacement, including quest includes validated by the helper. Missing or deleted map dependencies block Apply and build preflight. Each applied target has an immutable backup/history record; interrupted groups retain per-target evidence for reconciliation and reviewed completion/reversal. Unrelated external changes block replacement. Database and file commits are independent: a saved database operation is not replayed when a file subset is retried.

JSON preserves unchanged lexical tokens, unknown fields, precision and ordered duplicate members. New conflicting persisted identities are refused; existing duplicate multiplicities remain preserved. UTF-8/BOM and supported UTF-16 inputs retain their encoding; unsupported encoding needs explicit conversion into a reviewed draft. Seed shard order and generated spawn indexes are not stable identities. Database/seed comparison requires an unambiguous row match and never silently writes both destinations.

## Maps and collision

SMD editing preserves supported headers, ordered records, opaque padding and untouched tail bytes. Complete supported sections allow structural edits; truncated/unknown sections remain read-only. Terrain coordinates use the server's indexed grid and triangle queries. Client height uses a four-unit grid and bilinear interpolation, so matching vertices alone do not imply identical between-grid heights. Linked height review checks dimensions, spacing and explicit map association.

The SMD tools palette supports exact server-only X/Z/height edits, verified collision-section import from another complete SMD, and explicit collision-region translation. Region translation requires an operator-confirmed provenance JSON whose `sourceSha256` matches the displayed original SMD hash and whose `shapes` map covers the original shape/face assignments. Collision imports copy only the reviewed collision section. Enable **Server collision** to inspect that layer. Custom SMD names require an explicit terrain alias unless the filename matches; the audited stock Moradon2/zone21 association is retained.

Client height PNGs store exact float32 little-endian bits in RGBA8 channels. Supported PNGs are non-interlaced RGBA8, with validated CRC/inflation bounds; grid dimensions are 2–4097. Splat packing preserves high-bit tile indexes, directions and NoTile65535. Index255 is reserved by this client; indexes through8191 are represented, with 1–8192 vertically stacked square atlas layers. The in-editor ground preview limits GPU layers to512 and reports its fallback; source data retains all supported layers. Import a compatible three-file height/splat/atlas set or a bounded `.lkhr` float region.

Scenery placements, areas, water grids and effect placements preserve custom JSON fields. Structural edits and source markers operate on selected records; fresh captured map previews render actual source geometry. Scenery changes leave collision separate. KOCL supports finite triangle soup (up to four million triangles); transforms use explicit triangle selections and pivots. Large text/wireframe views are bounded and do not truncate saved data. Review affected bounds and shared zone mappings before Apply.

A client KOCL soup lacks original server shape/face provenance. Safe linked SMD collision rebuilding is restricted to verified layouts/provenance; unsupported region edits are refused. No guessed collision cells or automatic scenery-to-collision conversion are performed.

## NPC visuals and imports

The source editor handles NPC look-ID mappings, animation metadata/events, built-in plugs, plug-slot mappings, FX plugs and optional role effects. Disabled empty mappings remain valid. NPC and monster numbers may collide; database keys include their type. A shared model can serve multiple look IDs/prototypes. Cloning copies its model/metadata into new source paths and keeps referenced textures/plugs/effects shared until explicitly remapped.

Compatible imports support core glTF2 GLB/glTF, relative `.bin` buffers and compatible PNG dependencies. Required extensions, sparse accessors, remote/escaping/linked URIs and invalid transforms/bone/clip references are rejected. Imports are bounded to256 files and512MiB; individual source/PNG limits also apply. Supported core glTF is not a promise to convert arbitrary DCC, N3 or compressed custom formats. Existing dependent models/metadata are validated when resources are replaced.

The quick inspector renders source geometry and animation clips. **Preview captured source…** freezes draft candidates, builds a complete isolated client output and starts a new editor child before any installed-content catalog is loaded. That child validates output/provenance hashes, mounts the new pack and renders source attachments/FX. Selecting/editing sources invalidates stale captures. The parent retains its separately labelled installed reference. Preview/export cancellation and shutdown stop only owned children and keep ordinary database lookup independent.

## Quests, builds and runtime

Quest validation compiles staged scripts/includes and manifest membership in the helper without executing scripts. Include cycles, escapes and missing bindings produce diagnostics; valid source and explicit manifest membership use the same file review/history as other targets. This is configuration/source authoring, not a running game simulator.

Ordinary packaged editing includes its .NET runtimes. Source builds/previews additionally need configured Godot4.7.2 Mono, .NET SDK9 or later and a matching Windows Mono export template. A complete raw client project is required; an installed pack is not a substitute for missing raw GLBs/PNGs. Use a short separate build root. Windows Mono staging paths longer than145 characters are rejected to reserve space for generated toolchain files.

Builds copy immutable captured inputs, isolate generated imports/presets/solution data, compile/export a new full Windows client and validate executable, pack, managed/runtime files and hashes. A separate visual-only GPU probe checks raw data, terrain float bits and imported model geometry/bones/clips. Logs/manifests remain available on failure; a partial output is never reported verified. Recheck using `scripts/verify-client-output.ps1 -Manifest <build-manifest.json> -GodotEditor <Godot Mono executable>`.

Retail asset rebakes remain an external staging/comparison operation. Deploying new client output, reseeding the database and reloading/restarting the game server remain explicit operational steps outside the editor. In this server, force reseed/reload commands may overwrite direct database edits. Preserve your own deployment/maintenance workflow.

## Custom forks

Adapters cover the observed LibreKO schema and formats. Compatibility checks support missing content, mismatched schemas and custom unmapped columns within their declared limits. They cannot certify arbitrary rewritten code, enum meanings, same-size inventory-layout changes, custom scripts or different binary formats. Unknown IDs and fields stay visible and preserved; unsupported destructive writes are blocked.
