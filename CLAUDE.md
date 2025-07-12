# WorkbenchConnect RimWorld Mod

## Project Overview

RimWorld mod that enables connecting multiple workbenches to share bills between them, similar to storage connection system.

## Development Environment

- **RimWorld Version**: 1.6 (latest release)
- **Target Framework**: .NET Framework 4.7.2
- **Dependencies**: Harmony for patching
- **Assembly References**: Direct references to RimWorld 1.6 DLLs

## Build Commands

- **Build**: `./build.sh` (macOS/Linux) or `build.bat` (Windows)
- **Output**: `Assemblies/WorkbenchConnect.dll`

## Architecture

### Core Classes

1. **`IWorkbenchGroupMember`** - Interface for workbenches that can be connected
2. **`WorkbenchGroup`** - Manages shared bill stack and member workbenches
3. **`WorkbenchGroupUtility`** - UI utilities for linking/unlinking workbenches
4. **`WorkbenchGroupManager`** - Map-level manager for workbench groups

### Harmony Integration

- **`Building_WorkTable_Patches`** - Patches workbench to implement group membership
- **Key patches**: `GetGizmos`, `BillStack` property, `SpawnSetup`, `DeSpawn`, `ExposeData`

### Key Features Implemented

- Link/unlink workbench gizmos with targeting system
- Shared bill stack across connected workbenches
- Visual connection overlays (yellow lines)
- Automatic group management and cleanup
- Save/load persistence
- Configurable connection distance

## API Notes for RimWorld 1.6

- Use `LoadSaveMode.LoadingVars` instead of `LoadSaveMode.Loading`
- `Find.Targeter.BeginTargeting` requires explicit parameter list
- Convert `TargetInfo` to `LocalTargetInfo` using explicit cast: `(LocalTargetInfo)target`
- Use manual selection loop instead of `SelectInsideDragBox`

## File Structure

```
Source/WorkbenchConnect/
├── Core/
│   ├── IWorkbenchGroupMember.cs
│   ├── WorkbenchGroup.cs
│   ├── WorkbenchGroupUtility.cs
│   └── WorkbenchGroupManager.cs
├── Patches/
│   └── Building_WorkTable_Patches.cs
├── Utils/
│   └── DebugHelper.cs
└── WorkbenchConnectMod.cs (main entry point)

Languages/English/Keyed/
└── WorkbenchConnect.xml (localization)

Textures/UI/Commands/
└── placeholder_icons.txt (UI icon requirements)
```

## Settings

- `enableDebugLogging`: Toggle debug output
- `maxConnectionDistance`: Maximum distance for workbench connections (default: 10.0)

## Development Status

✅ Core infrastructure complete
✅ Harmony patches implemented  
✅ Build system working with RimWorld 1.6
⏳ Needs UI textures for gizmo icons
⏳ Requires in-game testing

## Next Steps

1. Create actual UI icon textures (LinkWorkbench.png, UnlinkWorkbench.png, SelectLinkedWorkbenches.png)
2. Test mod functionality in RimWorld 1.6
3. Optimize work assignment integration for grouped workbenches
4. Add visual polish and user feedback
