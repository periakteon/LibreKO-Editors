# Deploy server quests and includes

The editor validates staged quest scripts and manifest membership without running the game. Source saves do not update a deployed process or execute the quests.

1. Record the changed .quest scripts, included files, localization files and quest-manifest.json entries. Check NPC/zone/item bindings and include dependencies on staging.
2. Validate the complete staged script set and manifest. Correct all diagnostics, then Apply file changes and verify every target succeeded. Keep original backups and file-history records.
3. Back up the production quest directory and related database data. Arrange maintenance when changing active quest behavior. Deploy all reviewed dependencies together, not just the selected script.
4. Use the runtime GameServer:QuestsDirectory, normally Quests beside the server; respect an explicitly configured QuestManifest path. The supplied Docker publish includes supported quest files and the manifest in /app/Quests. Update the production source checkout, rebuild and recreate game when these files are baked into its image.
5. Before startup, complete the seed checks for any accompanying database changes. Use a controlled game-service restart to activate the release; do not rely on a notice from an unverified hot-reload command as proof that scripts were reloaded.
6. Inspect quest compilation/binding errors in production logs. With a test account, talk to the bound NPC and exercise each changed branch, required item check, reward and repeat/completion behavior. Check localization and a pre-existing quest state where applicable.
7. If the release fails, stop the service, restore the prior complete script/include/manifest set and reverse only separately reviewed database operations. Script rollback does not undo rewards already granted to player characters; those require an approved player-data recovery plan.

Changes to C# server policy or custom script opcodes require the corresponding server source build outside the quest editor. Custom forks can use other script loaders; verify their actual runtime roots and activation behavior.
