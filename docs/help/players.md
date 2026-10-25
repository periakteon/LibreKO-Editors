# Player characters and accounts in production

Use this workflow for inventory, equipment, appearance, character stats, money, GM role, Knight Cash, premium and VIP settings. Account settings affect all linked characters.

1. Identify the player's account and character IDs. Take a database backup or an approved targeted backup before editing live player data.
2. Have the player fully log out. Keep all characters on the linked account offline until the edit and verification are finished. The editor blocks online saves; a live session can also retain old values and later save over changes.
3. Check the selected database. If you edited a staging database, those changes are not in production. Connect explicitly to the intended production database and reapply the reviewed values there; the editor does not export or automatically promote a staging draft. If the original save already targeted production, do not apply it twice.
4. In Content, open Player characters for character/inventory edits or Accounts for account-wide role, Knight Cash, premium and VIP fields. Account role 0 is Game master, 1 is Normal player and 2 is Banned in this adapter. Dates use UTC.
5. Review the IDs, counts, item metadata and before/after values. Save to database and require a successful result. On conflict, reopen the current record and review it; do not overwrite a newer snapshot. On an unknown outcome, reconcile History before retrying.
6. Reopen the record from the database and confirm the saved values. Have the player log in again and check equipment, inventory, balance and permissions. A normal client asset update is unnecessary unless you also changed client visuals or definitions.
7. If the change is wrong, keep the account offline and use the matching database History operation to preview a guarded reversal, or your approved backup restore. Never restore a whole live database casually after other players have made progress.

Do not use reseed/reload game-data commands for player/account edits. They are unrelated to this workflow. Custom forks can use different role values or session caches; verify those meanings before changing permissions.
