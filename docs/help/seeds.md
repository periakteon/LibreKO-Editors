# Deploy database-backed server seed JSON

Use this for files such as Items.json, Npcs.json, NpcPositions.json, NpcItems.json, MakeItemGroups.json and event seeds. These files feed the database on startup; they are different from directly loaded merchant/service lists.

1. Identify every affected file, including numbered shards and referenced definitions. Review the complete logical dataset, not just the one edited row. The seeder can insert, update and delete rows according to each seed's policy; omitted custom rows can be removed.
2. Back up the production database and current deployed seed set. Compare production customizations with the edited source. The editor's source root may be a development checkout; Apply file changes only updates that selected root.
3. Validate and Apply file changes. Require successful results for each target. A partial result means only the successful subset was saved; reconcile file History before retrying. Preserve the operation record and hashes.
4. Test startup against a staging database copy with the exact new files and server build. Inspect the seed +added / updated / deleted counts and then verify affected IDs and unrelated custom rows. Do not ship a partial seed dataset.
5. Stop the production game service during maintenance. Deploy the complete reviewed file set under the game's actual Seed/Data runtime directory. For the supplied Docker image this is /app/Seed/Data, baked into the image; changing checkout files does not change a running container.
6. For the supplied Compose deployment, run the following from the production checkout containing docker-compose.yml after its source files are updated. Use your own service names if customized:

```text
docker compose stop game
docker compose build game
docker compose up -d --no-deps game
docker compose logs --tail 200 game
```

7. For a non-container deployment, publish the matching server into a separate release directory, include the complete reviewed Seed/Data set plus existing Map/Quests/configuration dependencies, and switch the stopped service to that release using your normal deployment procedure. A bare source-folder copy or dotnet publish alone does not prove a complete runtime deployment.
8. Start normally with forced seeding disabled. Changed fingerprints or row counts can still cause the appropriate seed to apply. Verify startup counts, database results and in-game behavior before ending maintenance. GameServer__Seeding__Force is the environment-variable form of the Force setting; false does not disable normal seeding.

Do not use docker compose down -v or remove database volumes. A seed rollback needs both the prior file/image set and a reviewed database recovery plan; restoring only an old file can itself trigger another reseed. Seed synchronization is explicit and separate from database saves.
