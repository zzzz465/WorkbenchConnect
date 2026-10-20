using System;
using HarmonyLib;
using RimWorld;

namespace WorkbenchConnect.Core;

internal static class BillSync
{
    private static int suppressDepth;

    private static readonly Func<Bill, bool> canCopy =
        AccessTools.MethodDelegate<Func<Bill, bool>>(AccessTools.PropertyGetter(typeof(Bill), "CanCopy"));

    /// <summary>True while the mod itself edits bill stacks, so the stack patches ignore those edits.</summary>
    public static bool Suppressed => suppressDepth > 0;

    public static Scope Suppress()
    {
        suppressDepth++;
        return default;
    }

    public readonly struct Scope : IDisposable
    {
        public void Dispose() => suppressDepth--;
    }

    /// <summary>Bills the game refuses to copy (e.g. ones tied to a specific corpse) stay on their own workbench.</summary>
    public static bool Linkable(Bill bill) => !bill.deleted && bill.recipe != null && canCopy(bill);

    public static Bill CloneInto(Bill source, BillStack target)
    {
        var clone = source.Clone();
        // Clone() leaves the load ID at -1; the game's paste does the same call.
        clone.InitializeAfterClone();
        target.AddBill(clone);
        return clone;
    }
}
