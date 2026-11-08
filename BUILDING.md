# Building USSEP CC Cleanup

Requirements: Visual Studio 2022 or newer (its C# compiler is used), and a Mod Organizer 2
profile with USSEP enabled. The real CC plugins are not needed for a build.

## Building the plugins

```
git clone https://github.com/hankinsrim/USSEPCCCleanup.git
cd USSEPCCCleanup
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Instance <MO2 instance> -ProfileName <profile>
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Instance <MO2 instance> -ProfileName <profile> -Zip
```

* `build.ps1` compiles `tool\*.cs` into `build\ccfree.exe` and runs `ccfree build`. Plugins
  are found through the profile's enabled mods and the game's Data folder from
  `ModOrganizer.ini`.
* `build\out\build-log.txt` lists every disabled, neutralized and cleaned record. Lines
  starting with `LEFTOVER` are CC references the build couldn't remove.
* `-Zip` also writes `build\USSEPCCCleanup-<version>.zip`, a minimal FOMOD whose
  `fomod\info.xml` carries the name and version for mod managers that read it.

The mod-manager-ready output is staged in `dist\`:

```
dist\
├── _ResourcePack.esl
├── ccbgssse001-fish.esm
├── ccbgssse025-advdsgs.esm
├── ccbgssse037-curios.esl
├── ccqdrsse001-survivalmode.esl
└── USSEP CC Cleanup.esp
```

## Updating for a new USSEP

The references to disable and the quest to stop are listed by form ID in `tool\Build.cs`, so a
new USSEP needs a check before rebuilding:

* Run `ccfree analyze <instance> <profile> analysis.txt` in a profile that still has the real CC
  plugins. It lists every USSEP record that points at CC content, with editor IDs.
* Compare it with the previous version's analysis, update `DisableRefs` and
  `StartQuestsToClear`, and rebuild. The build stops on a few cases it can't resolve and logs
  the rest as `LEFTOVER`; both need a look.
* Update `$ussepVersion` and `$version` in `build.ps1`, then test in game.

Other `ccfree` commands for investigating records: `dump`, `verify`, `diff`, `conds`, `find`,
`subs`, `orphans` and `scanlo`.
