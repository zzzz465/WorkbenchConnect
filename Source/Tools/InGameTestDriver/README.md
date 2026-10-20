# In-game test driver

A helper mod that runs scripted checks inside RimWorld 1.7 and writes `[WCTEST] PASS/FAIL`
lines to `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`.
It quits the game when done (`[WCTEST] DONE passes=N failures=M`).

Phases, carried across save/load by static fields:

1. Fresh quick-test map: link, add, settings, reorder, delete, repetition count, repeat cap with
   an unfinished thing, unlink, select linked, join an existing group. Saves `WCTest` and loads it.
2. Restored group and links, minify and destroy members. Rewrites `WCTest` into the pre-2.0
   layout as `WCTestLegacy` and loads it.
3. Migration of the legacy save, a resave, then real crafting: "Do 4 times" on three linked
   benches with every tailoring colonist must make exactly four shirts, with several benches
   busy at once.

## Running

1. Build the mod, then this project:
   `dotnet build Source/Tools/InGameTestDriver/InGameTestDriver.csproj -c Release`
2. Back up `Config/ModsConfig.xml`, `Prefs.xml` and `Knowledge.xml`.
3. Copy into `RimWorld/Mods`:
   - `WorkbenchConnectDev`: `About`, `Languages`, `1.7`, `LoadFolders.xml` from the repo, with the
     packageId in `About/About.xml` changed to `jungooji.workbenchconnect.dev` (the Workshop copy
     owns the real one) and `PublishedFileId.txt` removed.
   - `WCTestDriver`: the `Mod` folder of this project.
4. Set `ModsConfig.xml` active mods to `brrainz.harmony`, `ludeon.rimworld`,
   `jungooji.workbenchconnect.dev`, `jungooji.wctestdriver`. Do not use `-mod=`; it rewrites the
   mod list.
5. Run `RimWorld.exe -quicktest` and wait for `[WCTEST] DONE`.
6. Restore the three config files, delete both mod folders and the `WCTest*.rws` saves.
