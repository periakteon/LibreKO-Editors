# Contributing to LibreKO Editors

Bug reports, documentation corrections, compatibility reports and focused pull
requests are welcome. Contributions are distributed under the repository's GNU
AGPL v3 license. Preserve upstream attribution and do not submit proprietary
game assets, production dumps, account/player data or credentials.

## Set up a development checkout

1. Install Git, PowerShell 7.2+, .NET 10 SDK and Godot 4.7.2 **Mono**. Install
   matching Mono export templates if you will publish Windows packages.
2. Follow the sibling clone commands in README. Use the exact LibreKO commit
   in `dependencies.json`; updating a dependency requires explicit review.
3. Set `LIBREKO_GODOT` to the Mono console executable, or pass `-GodotEditor` to
   run/publish scripts. Packages restore from nuget.org.
4. Run `./scripts/build.ps1`, then `./scripts/run.ps1`. Configure local asset and
   workspace paths in Settings. Missing assets should leave database forms usable.

## Run checks

These checks need no game database or client packs:

```powershell
./scripts/test-tool-paths.ps1
./scripts/test-release-profile.ps1
dotnet test ./tests/LibreKO.Editors.Core.Tests --nologo
./scripts/build.ps1
./scripts/run.ps1 -Check boot
./scripts/run.ps1 -Check help
```

For integration tests, install native MySQL tools, including the server binary:

```powershell
$env:LIBREKO_MYSQL_BIN = 'C:/Tools/MySQL/bin'
try {
    ./scripts/test-database.ps1 -RunTests
} finally {
    ./scripts/test-database.ps1 -Stop
}
```

The fixture server binds `127.0.0.1:33307`, uses `.superpowers/mysql-data` and the
fixed `libreko_editor_test_native` schema with an insecure root account. It is
isolated development infrastructure: never expose it or point it at production.
The script checks physical datadir ownership and only aligns additive test
columns. It never migrates a profile's game database. Other modes may prepare
additional `libreko_editor_test_*` schemas in the same owned server.

Database tests share fixtures and disable parallel execution. Use meaningful
regressions for writes, validation, history replay, binary layouts and cancellation.
Schema/enum changes must preserve unrelated custom values or fail clearly.
Do not weaken a save/history guard to pass a test.

## Native UI and asset checks

Prepare fixtures after the database suite has created its test schema:

```powershell
. ./scripts/prepare-native-checks.ps1
./scripts/run.ps1 -Check events-calendar -Render
./scripts/run.ps1 -Check help -Render
./scripts/run.ps1 -Check shell-gallery -Render
./scripts/test-database.ps1 -Stop
```

Fixtures select an alternate profile and workspace. Some visual/3D checks require
your legally obtained packed client. Gallery checks write captures under
`artifacts`; do not commit artwork or screenshots containing personal data.
Broader authoring checks use `prepare-source-checks.ps1` and optional
`prepare-client-checks.ps1`; pass explicit fixture roots as documented by those
scripts. Never test source apply against live server/client directories.

## Package a Windows build

```powershell
./scripts/publish.ps1 -OutputDirectory './artifacts/LibreKO-Editors'
./scripts/verify-release.ps1 -ApplicationDirectory './artifacts/LibreKO-Editors' -Check boot -WithoutGlobalDotnet
./scripts/verify-release.ps1 -ApplicationDirectory './artifacts/LibreKO-Editors' -Check help -WithoutGlobalDotnet
./scripts/package-release.ps1 -ApplicationDirectory './artifacts/LibreKO-Editors' -ZipPath './artifacts/LibreKO-Editors-win-x64.zip'
```

Use an empty output directory and close any app using it. Standard Godot Mono
export templates are used; a game executable is not a template dependency.
The ZIP uses an explicit allowlist and omits profiles, workspaces, fixtures and
credentials. Include exact editor/game source, dependency pin, licenses, notices
and checksums when distributing. Verify extracted packages as well as source.

## Make a pull request

1. Fork the repository and create a focused branch from `main`.
2. Describe the concrete problem, expected behavior and supported LibreKO revision.
3. Add a regression for behavior changes; run relevant suites and build. Report
   commands/results and any check you could not run.
4. Update production/user Help when activation, save behavior or deployment changes.
5. Keep database, source-file and preview operations clear in the UI. Preserve
   drafts across cancelled navigation and loading failures.
6. Open a PR using the template. Avoid unrelated formatting or dependency upgrades.

CI builds the solution and runs Core/tool-path checks. Database, GPU and packaged
checks require additional tools and are not implied by a green Core job.

Bug reports should include editor/game versions, database engine, reproduction
steps and sanitized errors. Describe custom columns without uploading dumps or
packs. Remove connection strings, tokens, player identifiers and personal paths.
