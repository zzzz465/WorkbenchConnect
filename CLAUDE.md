# WorkbenchConnect RimWorld Mod

RimWorld mod that links workbenches of the same type so they share bills, similar to linked
storage settings. README.md describes the behaviour, build and release steps.

## Layout

- `1.6/Assemblies` holds the last 1.6 build. It is not rebuilt; its source is commit `0882b03`.
- `1.7/Assemblies` is built from `Source/WorkbenchConnect` (net10.0, RimWorld 1.7 / Waylith,
  Harmony 3.0). The DLL is committed.
- `Source/WorkbenchConnect.Tests` (xunit.v3) compiles `Linking/` directly and never loads the game.

## Code

- `Linking/LinkTable.cs` — engine-independent bookkeeping: one row per shared bill, one copy
  per member. `Reconcile` returns a plan (copies to delete, rows to clone into members);
  `DesiredOrder` gives each member's list in row order; `FindChanged`/`Restamp` detect which
  copy's settings moved. Keep RimWorld types out of this folder so the tests can compile it.
- `Core/WorkbenchGroup.cs` — a group: carries out plans with game calls (`Sync`), copies
  settings (`SyncSettings`), counts repetitions, and saves itself (members and rows as
  references; the benches save their own bills).
- `Core/WorkbenchGroupManager.cs` — map component holding the groups. Its full type name,
  `WorkbenchGroup`'s type name and the `groups`/`loadID`/`nextGroupID` keys are part of the
  save format.
- `Core/LegacySave.cs` — converts pre-2.0 saves (bills in the group's `savedBills`, empty
  member stacks, `workbenchGroupID_<thingID>` on each bench).
- `Core/BillSettings.cs` — hash and copy of the fields `Bill.Clone`/`Bill_Production.Clone` copy.
- `Core/BillSync.cs` — suppression flag for the mod's own stack edits, clone helper.
- `Core/GroupRegistry.cs` — bench → group lookup (ConditionalWeakTable).
- `Patches/Patches.cs`, `UI/GroupGizmos.cs` — Harmony patches, gizmos and the selection overlay.

## Notes on RimWorld 1.7

- `Bill.Clone()` leaves the load ID at -1; call `InitializeAfterClone()` like the game's paste does.
- `WorkGiver_DoBill` skips a bill whose unfinished thing is bound to another pawn, which is why
  members hold copies instead of one shared bill.
- `BillStack.RemoveIncompletableBills` removes bills without `Delete`; the stack patch and the
  periodic `Sync` handle it.
- `dotnet test` currently runs 0 tests on this machine (also in other repos); run the test
  executable with `dotnet run --project Source/WorkbenchConnect.Tests` instead.
