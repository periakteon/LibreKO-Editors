# Start here: where did my changes go?

Production means the server and client used by real players. Staging means a separate test installation. The editor does not know whether your selected database or source folder is production.

## Choose your workflow

1. Check the database name in the header and the connection you selected. Check the source profile and roots in Sources. A local database or source folder is not automatically copied to production.
2. Identify the destination in Review changes. DATABASE means the connected database. SOURCE means the exact file in the selected source root. A mixed review has separate save buttons and separate results.
3. For character inventory, account role, Knight Cash or premium, use Player characters & accounts.
4. For item stats, NPCs, drops, spawns, zones or event configuration saved to the database, use Database configuration.
5. For server JSON files, choose Server seed JSON or Merchant shops & service files. For SMD or quests, choose Server maps or Server quests.
6. For icons, models, effects, client definitions or terrain, use Client release. If server and client must change together, use Combined release & rollback.

## Saving and activation are different

An unsaved draft changes only the editor preview. Save to database commits to the selected database; Apply file changes writes the selected source files. Neither action deploys a server image, uploads files, restarts services, distributes a client or updates client catalogues.

History stores editor operations and recovery evidence. It is not a full production database backup. Preview captured source is a visual test, not a production release. Build client creates a separate output, not a player installation update.

## Before a production change

Back up the live database and affected runtime files. Test the whole workflow on staging with a copy of the production data. Record the changed IDs, files, backup locations and release version. Arrange maintenance when cached game data or map/client compatibility changes. Use the actual deployment roots and service names for your installation; the examples describe this checkout.
