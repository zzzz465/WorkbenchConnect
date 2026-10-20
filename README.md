# Workbench Connect

RimWorld mod that lets linked workbenches share bills.

Select two or more workbenches of the same type and press **Link workbench**. From then on,
adding, deleting, reordering or editing a bill on one of them does the same on the others.

## How it works (1.7)

- Every linked workbench keeps its own copy of each shared bill. The game does not let a
  second pawn start a bill whose unfinished item is in progress, so one shared bill object
  would let only one crafter work at a time. With copies, several pawns work the same bill at
  different benches.
- Adding, deleting and reordering are copied right away through patches on `BillStack`.
- Settings are edited through plain field writes all over the bill UI, so the mod compares a
  hash of each copy's settings every 10 frames and copies the one that changed.
- "Do X times" counts across the group. A finished repetition lowers the count everywhere, and
  a pawn does not start a copy when the copies already in progress cover every remaining
  repetition.
- Linking unlinked benches pools all their bills. A bench joining an existing group drops its
  own bills and takes the group's. Unlinking, minifying or destroying a bench leaves its copies
  on it as ordinary bills.
- Each workbench saves its own bills; the group only saves references to the copies. Removing
  the mod leaves every workbench with its bills.
- Saves from 1.x versions (shared bills stored in the group) are converted on load.

## Versions

| Game | Folder | Source |
| --- | --- | --- |
| 1.6 | `1.6/Assemblies` | Last 1.6 build, kept as is (commit `0882b03`). Not rebuilt. |
| 1.7 | `1.7/Assemblies` | `Source/WorkbenchConnect`, built here. |

`LoadFolders.xml` picks the folder for the running game version.

## Build

RimWorld 1.7 has no reference package on NuGet, so the build compiles against the game
itself. `Source/Directory.Build.props` finds it at the default Steam path when that install is
on the 1.7 branch. Otherwise create `Source/Local.props`:

```xml
<Project>
  <PropertyGroup>
    <RimWorld17Dir>D:\Games\RimWorld</RimWorld17Dir>
  </PropertyGroup>
</Project>
```

```bash
dotnet build Source/WorkbenchConnect/WorkbenchConnect.csproj -c Release
```

The DLL goes to `1.7/Assemblies/WorkbenchConnect.dll` and is committed.

## Tests

`Source/WorkbenchConnect.Tests` covers the engine-independent bookkeeping in
`Source/WorkbenchConnect/Linking`.

```bash
dotnet run --project Source/WorkbenchConnect.Tests
```

## Package For Steam Workshop

```bash
scripts/package-workshop.sh
```

This creates `dist/WorkbenchConnect` with only the files RimWorld needs: `About`,
`Languages`, `1.6`, `1.7` and `LoadFolders.xml`.

## Publish To Steam Workshop

The existing Workshop item is tracked by `About/PublishedFileId.txt`.

```bash
scripts/publish-workshop.sh \
  --steam-user YOUR_STEAM_LOGIN \
  --changenote "Describe this update"
```

`scripts/publish-workshop.sh` writes `dist/workshop_item.vdf` before upload because
SteamCMD's `workshop_build_item` command reads Workshop metadata from a VDF file.
SteamCMD does not update the Workshop version tags; the 1.7 tag has to be set once with the
in-game uploader.
