# LibreKO Editors v2026.10.25

Initial public Windows x64 release of the Godot/C# editor suite.

- Content: items, character visuals/inventory and separate account management.
- Events: supported schedules, rewards, world events and NPC triggers, including
  daily/weekly/once/monthly calendar rules and strict date entry.
- Map & NPC: database forms, interactive 3D maps, spawn focus, visual shops/loot,
  source authoring, navigation and loading feedback.
- Searchable Help/F1 with production update and rollback steps.
- Portable developer tools and a pinned LibreKO source dependency.

Extract the entire Windows archive, run LibreKO.Editors.exe, configure your own
client assets/workspace in Settings and connect to staging before production.
The runtime includes .NET and excludes assets, profiles, histories and secrets.

Compatibility targets LibreKO e5f0e4200fd42f69934f55557f92f2ee2ccfafde. Custom forks
may change semantics beyond structural validation. Table whitelists do not
automatically expose every new upstream feature. Calendar overlap warnings may
be inaccurate for date-based rules; they are advisory and do not block saves.

Read Help before restarting/reloading: forced reseeding can overwrite database
edits, and ordinary row-count changes can also trigger reseeding. Merchant changes
need matching client catalogues and distribution. The editor does not deploy or
restart services automatically.

Source is AGPL v3; dependencies retain their licenses. The release includes
source/dependency references, notices and SHA256 checksums. Proprietary assets
and asset-bearing screenshots are not distributed.
