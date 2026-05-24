# Workbench Connect

RimWorld mod that lets linked workbenches share bills.

## Prerequisites

- .NET SDK available as `dotnet`
- SteamCMD available as `steamcmd` for publishing

## Local Build

```bash
./build.sh
```

The build uses NuGet reference assemblies from `Krafs.Rimworld.Ref` and
`Lib.Harmony.Ref`, so RimWorld and Harmony do not need to be installed locally
for compilation.

The compiled DLL is written to `Assemblies/WorkbenchConnect.dll`.

## Package For Steam Workshop

```bash
scripts/package-workshop.sh
```

This creates `dist/WorkbenchConnect` and copies only the files RimWorld needs:
`About`, `Assemblies`, `Languages`, and any optional content folders such as
`Defs` or `Textures`. Source files and build artifacts are not included.

## Publish To Steam Workshop

The existing Workshop item is tracked by `About/PublishedFileId.txt`.

Run an initial SteamCMD login once if Steam Guard is required:

```bash
steamcmd +login YOUR_STEAM_LOGIN +quit
```

Then publish locally:

```bash
scripts/publish-workshop.sh \
  --steam-user YOUR_STEAM_LOGIN \
  --changenote "Describe this update"
```

Useful overrides:

```bash
STEAMCMD="/path/to/steamcmd" \
STEAM_USER="YOUR_STEAM_LOGIN" \
CHANGE_NOTE="Describe this update" \
scripts/publish-workshop.sh
```

`scripts/publish-workshop.sh` writes `dist/workshop_item.vdf` before upload
because SteamCMD's `workshop_build_item` command reads Workshop metadata from a
VDF file.
