using HarmonyLib;
using RimWorld;
using Verse;
using WorkbenchConnect.Core;

namespace WorkbenchConnect.Patches
{
    public static class BillStack_Patches
    {
        public static void Patch(Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(BillStack), nameof(BillStack.AddBill)),
                postfix: new HarmonyMethod(typeof(BillStack_Patches), nameof(BillStackChanged_Postfix)));

            harmony.Patch(
                AccessTools.Method(typeof(BillStack), nameof(BillStack.Delete)),
                postfix: new HarmonyMethod(typeof(BillStack_Patches), nameof(BillStackChanged_Postfix)));

            harmony.Patch(
                AccessTools.Method(typeof(BillStack), nameof(BillStack.Clear)),
                postfix: new HarmonyMethod(typeof(BillStack_Patches), nameof(BillStackChanged_Postfix)));

            harmony.Patch(
                AccessTools.Method(typeof(BillStack), nameof(BillStack.Reorder)),
                postfix: new HarmonyMethod(typeof(BillStack_Patches), nameof(BillStackChanged_Postfix)));

            harmony.Patch(
                AccessTools.Method(typeof(Bill), nameof(Bill.DoInterface)),
                postfix: new HarmonyMethod(typeof(BillStack_Patches), nameof(BillInterface_Postfix)));

            harmony.Patch(
                AccessTools.Method(typeof(Dialog_BillConfig), nameof(Dialog_BillConfig.DoWindowContents)),
                postfix: new HarmonyMethod(typeof(BillStack_Patches), nameof(DialogBillConfig_Postfix)));
        }

        public static void BillStackChanged_Postfix(BillStack __instance)
        {
            SyncFromBillStack(__instance, force: true);
        }

        public static void BillInterface_Postfix(Bill __instance)
        {
            SyncFromBillStack(__instance?.billStack, force: false);
        }

        public static void DialogBillConfig_Postfix(Bill_Production ___bill)
        {
            SyncFromBillStack(___bill?.billStack, force: false);
        }

        private static void SyncFromBillStack(BillStack billStack, bool force)
        {
            if (WorkbenchGroup.IsSynchronizingBills)
                return;

            if (billStack?.billGiver is not Building_WorkTable workTable)
                return;

            var member = Building_WorkTable_Patches.GetMemberData(workTable);
            if (member?.Group == null)
                return;

            if (force)
            {
                member.Group.SyncBillsFromMember(member);
            }
            else
            {
                member.Group.SyncBillsFromMemberIfChanged(member);
            }
        }
    }
}
