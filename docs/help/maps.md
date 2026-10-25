# Deploy server maps: SMD height, collision, events and warps

Server SMD controls server-side map behavior. Client terrain controls what players see. Editing one does not automatically deploy the other. Legacy database Warps rows are not the live SMD warp destinations in this server.

1. Record the zone ID, SMD filename and any shared-map/terrain alias. Review all zones sharing that resource. Test movement, collision and warps in staging with the same server/client pair.
2. Validate and Apply file changes for the edited SMD. Save related zone, spawn, event and trigger database changes separately. Keep the original SMD backup and database operation records.
3. Back up the production map files and database. Arrange maintenance and remove players from affected zones or stop the game service. The conservative activation path is a full game-service restart.
4. Deploy the reviewed SMD under GameServer:MapDirectory, normally Map beside the server. The supplied Docker image uses /app/Map and bakes the files during build. Update the production checkout, rebuild and recreate game; restarting an unchanged image does not deploy a new SMD.
5. If ZoneInfos or other database configuration changed, deploy it using Database configuration. Check seed alignment before startup, including insert/delete row-count changes.
6. If visible terrain, scenery, client collision or FX also changed, build and distribute the matching client release before players enter. Matching height vertices alone does not guarantee identical server/client between-grid heights. Client collision geometry is not an automatic SMD collision rebuild.
7. Start the server and inspect map-load diagnostics. Verify the exact zone and edited coordinates in-game: walking/height, collision, spawn positions, event triggers and both ends of each warp. Check another zone if the SMD is shared.
8. If behavior is wrong, stop the service and restore the coordinated prior SMD, database changes and client version as applicable. Test the restored pair before reopening the zone.

Unsupported SMD sections or missing collision provenance stay read-only. A successful editor preview does not certify every game physics or quest interaction; validate the deployed runtime.
