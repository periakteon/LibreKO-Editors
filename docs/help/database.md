# Database configuration: items, NPCs, drops, spawns and events

This workflow covers Save to database in Items, Events and Map & NPC. The running game caches configuration and existing world entities can retain old state. This guide uses a controlled restart for activation.

## Event calendar schedules

Choose Repeat by its named value: Daily, Weekly, Once or Monthly. Weekly requires
a weekday; Once requires a date entered as yyyy-MM-dd; Monthly requires a day
from 1 to 31. In shorter months the server uses the last day. Date is a calendar
date, without timezone conversion. The overview separates once/monthly rules
from its recurring week. Collection Race and Lottery use UTC; Temple uses the
server schedule clock shown in the editor. Existing unknown custom values stay
visible, but the editor cannot interpret their runtime behavior.

## Production steps

1. Record the changed tables, typed NPC/monster IDs, zones and dependencies. Back up the live database and the server's deployed Seed/Data files. Test on staging using the same server build and seed set.
2. Before any restart, compare the deployed seed definitions with the database changes. Startup checks both seed fingerprints and the tracked table row count. Adding or deleting database rows can trigger reseeding even when the seed files are unchanged. A missing seed-state record, a changed seed fingerprint or GameServer:Seeding:Force=true can also reapply JSON values.
3. Resolve that conflict before proceeding. For supported data, use Compare seed and the corresponding source JSON editor to align the complete seed definitions with the desired database state. Include intentional deletions and unrelated live customizations. Compare seed is a review tool, not an automatic export. If your fork uses a different seeding policy, have its operator verify that policy on staging. Merely setting Force=false does not disable normal fingerprint/row-count reseeding.
4. Enter maintenance and stop the affected game service so players and world activity cannot race the change. Keep the database volume/data. If changes were only made on staging, explicitly connect the editor to production and reapply the reviewed database operations. If already saved to production, verify them rather than repeating them.
5. Deploy any reconciled seed files using Server seed JSON instructions. Leave forced seeding disabled for ordinary startup. Do not alter SeedState fingerprints or counts manually to disguise a mismatch.
6. Start the affected game service. Inspect startup logs for unexpected seed updates/deletions and failed map/event loads. Reopen the affected database rows after startup and verify that the desired values survived.
7. Log in with a test character. Check the item or NPC, the exact spawn location, drops/shop links or event schedule. Existing spawned objects and running events are not guaranteed to update from a cache reload alone. Reopen access only after the checks pass.
8. If client-visible names, icons, models, equipment definitions or terrain changed too, complete Client release before admitting incompatible clients. Database editing never regenerates assets/items/items.json or client packs.

## Important: the reload aliases also write the database

In this checkout, +reloadgamedata, +reloaddrops and +reseed all force every configured JSON seed before reloading game data. They are not database-only cache refresh commands and can overwrite your edits or remove rows. Do not use them as a shortcut for this workflow. Restart also requires the seed checks above.
