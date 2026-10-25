# Release updated client visuals, definitions and terrain

Use this for icons, equipment/NPC models, animations, attachments, visual effects, client definitions and maps. A database save does not rebuild client assets or assets/items/items.json. The editor's installed client setting supplies reference previews; changing it does not update players.

## Build and verify

1. In Sources, select the intended raw ClientProject and a separate short BuildOutput directory. Configure Godot4.7.2 Mono, a compatible .NET SDK and the matching Windows Mono export template. An installed .pck file is not a replacement for missing raw sources.
2. Validate the edited source files and dependencies. Use Apply file changes and require all intended files to be saved. A captured preview can include drafts and is not proof that the normal release build contains them.
3. If item names, model/icon IDs or visual definitions changed, update the corresponding supported client source definitions or use your fork's authoring tools. For this client, item definitions include assets/items/items.json and icons use assets/items/icons/<icon>.png. Database IDs and client definitions must agree. Merchant stock membership and placement changes also require its _sell dictionary to match server ItemSells.json: each SellingGroup string key contains [ItemId, Line, Index] entries. Add/remove/move the same entries in both destinations, preserve unrelated groups, then rebuild and distribute this client together with the server shop change. Applying ItemSells.json does not regenerate _sell, and the retail baker reads itemsell_table.tbl rather than that server JSON. The editor does not automatically export the database or merchant stock into the client catalogue.
4. Use Preview captured source to inspect the edited model, FX or map. Then use Build client and Save drafts & build. Resolve tool, import and dependency failures; do not distribute a failed or partially built folder.
5. Require Verified client output. Inspect build-manifest.json, its artifact hashes and the independent GPU verification result. Open output to locate the exact release directory. Recheck it with the editor repository's scripts/verify-client-output.ps1 if the output has been moved or changed.

## Update production clients

6. Back up the current distributed client and its release manifest. Stage the complete verified output as a new version, preserving the executable, main .pck, .NET/runtime folders and all required resources. Do not copy only a PNG, GLB, executable or main pack and assume the player installation is complete.
7. Confirm whether the new build includes all content or still requires matching content/terrain.pck, characters.pck, armor.pck, weapons.pck, npcs.pck and objects.pck. The client can mount external content packs and later patch packs; old overrides may replace new resources. Test a clean install and the real updater path, including downloaded content/patch caches. Do not tell players to delete arbitrary user data.
8. Publish the complete version through your actual installer, download site or launcher/update system. This editor does not upload releases, generate legacy patches, register a launcher package or deploy to players. A successful local build is not a published update.
9. If the server change requires the new client, deploy the coordinated server changes and distribute the client in one maintenance window. GameServer:MinimumClientVersion can gate logins in this checkout, but setting it does not distribute files. Use a real version value emitted by your release process; do not invent one or lock out players before the update is available.
10. From a clean player installation, log in to production with a test account. Inspect the exact changed item/NPC, inventory tooltip, effects, map movement/collision and relevant gameplay. Also test updating an existing installation. Reopen access after both succeed.
11. Restart the editor after changing its installed reference build or packs, select the new installation in Settings and rescan Compatibility. Otherwise its reference preview can still show old assets even though the deployed client is new.

## Rollback

Keep the previous complete client version available. If you roll back a coordinated release, restore the matching server/database configuration and client-version gate too. A server rollback alone cannot make incompatible client map/asset definitions safe. Retail/N3 conversion remains an external asset-pipeline operation; run it into staging rather than rebaking original assets in place.
