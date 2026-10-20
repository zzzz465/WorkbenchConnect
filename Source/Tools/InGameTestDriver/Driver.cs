using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;
using WorkbenchConnect.Core;

namespace WCTestDriver;

public class DriverMod : Mod
{
    public DriverMod(ModContentPack content) : base(content)
    {
        Log.Message("[WCTEST] driver loaded");
    }
}

/// <summary>Scripted checks. Static state survives the save/load round trips between phases.</summary>
public class DriverComponent : GameComponent
{
    private static int phase;
    private static int failures;
    private static int passes;
    private static int wait = 120;
    private static Game? leaving;
    private static readonly Queue<(string Name, Action Run)> steps = new();
    private static bool planned;
    private static int idA, idB, idC;
    private static Dictionary<int, string> savedRecipes = new();

    private static readonly MethodInfo registryOf = AccessTools.Method(
        AccessTools.TypeByName("WorkbenchConnect.Core.GroupRegistry"), "Of", new[] { typeof(Building_WorkTable) });

    public DriverComponent(Game game)
    {
    }

    public override void GameComponentUpdate()
    {
        if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || Current.Game == leaving)
            return;
        if (wait > 0)
        {
            wait--;
            return;
        }
        if (!planned)
        {
            planned = true;
            Plan();
        }
        if (steps.Count == 0)
            return;

        var (name, run) = steps.Dequeue();
        try
        {
            Log.Message($"[WCTEST] step {phase}:{name}");
            run();
        }
        catch (Exception e)
        {
            Fail($"{name} threw {e}");
            steps.Clear();
            Finish();
        }
    }

    // ---------------------------------------------------------------- plan

    private static void Plan()
    {
        switch (phase)
        {
            case 0: PlanFresh(); break;
            case 1: PlanReloaded(); break;
            case 2: PlanLegacy(); break;
        }
    }

    private static void Step(string name, Action run) => steps.Enqueue((name, run));

    private static void Wait(int frames) => Step("wait", () => wait = frames);

    private static Map Map => Find.CurrentMap;
    private static Building_WorkTable A => Bench(idA);
    private static Building_WorkTable B => Bench(idB);
    private static Building_WorkTable C => Bench(idC);
    private static Pawn Colonist => Map.mapPawns.FreeColonists.First();

    private static void PlanFresh()
    {
        Step("spawn", () =>
        {
            var def = DefDatabase<ThingDef>.GetNamed("HandTailoringBench");
            var center = Map.Center;
            idA = Spawn(def, center).thingIDNumber;
            idB = Spawn(def, center + new IntVec3(5, 0, 0)).thingIDNumber;
            idC = Spawn(def, center + new IntVec3(10, 0, 0)).thingIDNumber;
            A.billStack.AddBill(NewBill("BasicShirt"));
            B.billStack.AddBill(NewBill("Pants"));
            Check(GroupOf(A) == null, "fresh bench has no group");
        });
        Wait(2);

        Step("link disabled for one bench", () =>
        {
            var link = Gizmo(A, "WorkbenchConnect.LinkWorkbench", A);
            Check(link.Disabled, $"link disabled with one bench ({link.disabledReason})");
            Check(GizmoOrNull(A, "WorkbenchConnect.UnlinkWorkbench", A) == null, "no unlink gizmo before linking");
        });

        Step("link three", () => Click(A, "WorkbenchConnect.LinkWorkbench", A, B, C));
        Wait(2);
        Step("verify link", () =>
        {
            var group = GroupOf(A);
            Check(group != null && GroupOf(B) == group && GroupOf(C) == group, "all three in one group");
            Check(group?.Members.Count == 3, "group has three members");
            ExpectAll("BasicShirt,Pants", "bills pooled on link");
            Check(!ReferenceEquals(A.billStack[0], B.billStack[0]), "copies are separate objects");
            CheckUniqueLoadIds("after link");
            var link = Gizmo(A, "WorkbenchConnect.LinkWorkbench", A, B, C);
            Check(link.Disabled, $"link disabled when already linked ({link.disabledReason})");
        });

        Step("add on B", () => B.billStack.AddBill(NewBill("Duster")));
        Wait(2);
        Step("verify add", () =>
        {
            ExpectAll("BasicShirt,Pants,Duster", "added bill copied");
            CheckUniqueLoadIds("after add");
        });

        Step("edit settings on C", () =>
        {
            var shirt = (Bill_Production)C.billStack[0];
            shirt.repeatCount = 7;
            shirt.suspended = true;
            shirt.ingredientSearchRadius = 30f;
            shirt.ingredientFilter.SetAllow(ThingDefOf.Cloth, false);
            shirt.SetStoreMode(BillStoreModeDefOf.DropOnFloor);
            shirt.RenamableLabel = "Renamed shirt";
        });
        Wait(15);
        Step("verify settings", () =>
        {
            foreach (var bench in new[] { A, B })
            {
                var shirt = (Bill_Production)bench.billStack[0];
                Check(shirt.repeatCount == 7, $"{bench} repeat count synced ({shirt.repeatCount})");
                Check(shirt.suspended, $"{bench} suspend synced");
                Check(Math.Abs(shirt.ingredientSearchRadius - 30f) < 0.01f, $"{bench} radius synced");
                Check(!shirt.ingredientFilter.Allows(ThingDefOf.Cloth), $"{bench} filter synced");
                Check(shirt.GetStoreMode() == BillStoreModeDefOf.DropOnFloor, $"{bench} store mode synced");
                Check(shirt.RenamableLabel == "Renamed shirt", $"{bench} name synced");
            }
        });

        Step("reorder on A", () => A.billStack.Reorder(A.billStack[0], 1));
        Wait(2);
        Step("verify reorder", () => ExpectAll("Pants,BasicShirt,Duster", "reorder copied"));

        Step("delete on B", () => B.billStack.Delete(B.billStack[2]));
        Wait(2);
        Step("verify delete", () => ExpectAll("Pants,BasicShirt", "delete copied"));

        Step("unsuspend and count", () =>
        {
            ((Bill_Production)A.billStack[1]).suspended = false;
        });
        Wait(15);
        Step("iteration", () =>
        {
            var shirt = (Bill_Production)A.billStack[1];
            Check(!((Bill_Production)C.billStack[1]).suspended, "unsuspend synced");
            shirt.Notify_IterationCompleted(Colonist, new List<Thing>());
            foreach (var bench in new[] { A, B, C })
                Check(((Bill_Production)bench.billStack[1]).repeatCount == 6, $"{bench} counted the repetition ({((Bill_Production)bench.billStack[1]).repeatCount})");
        });

        Step("repeat cap", () =>
        {
            ((Bill_Production)A.billStack[1]).repeatCount = 1;
        });
        Wait(15);
        Step("verify repeat cap", () =>
        {
            var copyA = (Bill_ProductionWithUft)A.billStack[1];
            var copyC = (Bill_ProductionWithUft)C.billStack[1];
            Check(copyC.repeatCount == 1, "count 1 synced");
            Check(copyA.PawnAllowedToStartAnew(Colonist), "free bill can start");
            var uft = (UnfinishedThing)ThingMaker.MakeThing(copyC.recipe.unfinishedThingDef, ThingDefOf.Cloth);
            uft.BoundBill = copyC;
            Check(!copyA.PawnAllowedToStartAnew(Colonist), "blocked while the only repetition is in progress at C");
            Check(copyC.PawnAllowedToStartAnew(Colonist), "C itself can continue its unfinished thing");
            copyA.repeatCount = 2;
            BillCopyAll(copyA);
            Check(copyA.PawnAllowedToStartAnew(Colonist), "second repetition can start elsewhere");
            uft.BoundBill = null;
            uft.Destroy();
        });

        Step("unlink C", () => Click(C, "WorkbenchConnect.UnlinkWorkbench", C));
        Wait(2);
        Step("verify unlink", () =>
        {
            Check(GroupOf(C) == null, "C left");
            Check(GroupOf(A) != null && GroupOf(A) == GroupOf(B), "A and B still linked");
            Check(Recipes(C) == "Pants,BasicShirt", $"C kept its bills ({Recipes(C)})");
            C.billStack.AddBill(NewBill("TribalA"));
        });
        Wait(2);
        Step("verify unlinked bench is independent", () =>
        {
            Check(Recipes(A) == "Pants,BasicShirt", $"A unaffected by C ({Recipes(A)})");
        });

        Step("select linked", () =>
        {
            Click(A, "WorkbenchConnect.SelectLinked", A);
            Check(Find.Selector.IsSelected(B), "select linked selects B");
        });

        Step("C joins existing group", () => Click(C, "WorkbenchConnect.LinkWorkbench", A, C));
        Wait(2);
        Step("verify join", () =>
        {
            Check(GroupOf(C) == GroupOf(A), "C joined A's group");
            ExpectAll("Pants,BasicShirt", "joining bench takes the group's bills");
            CheckUniqueLoadIds("after join");
        });

        Step("save and reload", () =>
        {
            Find.Selector.ClearSelection();
            savedRecipes = new[] { A, B, C }.ToDictionary(b => b.thingIDNumber, Recipes);
            GameDataSaveLoader.SaveGame("WCTest");
            phase = 1;
            Reload("WCTest");
        });
    }

    private static void PlanReloaded()
    {
        Step("verify restore", () =>
        {
            var group = GroupOf(A);
            Check(group != null && GroupOf(B) == group && GroupOf(C) == group, "group restored");
            foreach (var bench in new[] { A, B, C })
                Check(Recipes(bench) == savedRecipes[bench.thingIDNumber], $"{bench} bills restored ({Recipes(bench)})");
            CheckUniqueLoadIds("after load");
            A.billStack.AddBill(NewBill("Duster"));
            ((Bill_Production)B.billStack[0]).repeatCount = 4;
        });
        Wait(15);
        Step("verify links after load", () =>
        {
            ExpectAll("Pants,BasicShirt,Duster", "links work after load");
            Check(((Bill_Production)C.billStack[0]).repeatCount == 4, "settings sync after load");
        });

        Step("minify B", () => B.MakeMinified());
        Wait(2);
        Step("verify minify", () =>
        {
            Check(GroupOf(A) != null && GroupOf(A) == GroupOf(C) && GroupOf(A)!.Members.Count == 2, "minified B left, A and C stay linked");
        });

        Step("destroy C", () => C.Destroy());
        Wait(2);
        Step("verify dissolve", () =>
        {
            Check(GroupOf(A) == null, "group dissolved with one bench left");
            Check(Recipes(A) == "Pants,BasicShirt,Duster", $"A kept its bills ({Recipes(A)})");
        });

        Step("write legacy save", () =>
        {
            WriteLegacySave("WCTest", "WCTestLegacy");
            phase = 2;
            Reload("WCTestLegacy");
        });
    }

    private static void PlanLegacy()
    {
        Step("verify migration", () =>
        {
            var group = GroupOf(A);
            Check(group != null && GroupOf(B) == group && GroupOf(C) == group, "legacy group migrated");
            foreach (var bench in new[] { A, B, C })
                Check(Recipes(bench) == savedRecipes[bench.thingIDNumber], $"{bench} legacy bills restored ({Recipes(bench)})");
            CheckUniqueLoadIds("after migration");
            Check(((Bill_Production)B.billStack[1]).repeatCount == ((Bill_Production)A.billStack[1]).repeatCount, "legacy settings copied");
            C.billStack.AddBill(NewBill("Duster"));
        });
        Wait(2);
        Step("verify links after migration", () => ExpectAll("Pants,BasicShirt,Duster", "links work after migration"));

        Step("resave", () => GameDataSaveLoader.SaveGame("WCTestResaved"));
        Wait(30);
        PlanCrafting();
    }

    // Real pawns, real jobs: "Do 4 times" on three linked benches must make exactly four shirts,
    // with more than one bench busy at once.
    private static int completions;
    private static int maxConcurrent;
    private static int craftStartTick;

    private static void PlanCrafting()
    {
        Step("prepare crafting", () =>
        {
            new Harmony("jungooji.wctestdriver").Patch(
                AccessTools.Method(typeof(Bill_Production), nameof(Bill_Production.Notify_IterationCompleted)),
                postfix: new HarmonyMethod(typeof(DriverComponent), nameof(CountCompletion)));

            foreach (var bill in A.billStack.Bills.ToList())
                A.billStack.Delete(bill);
            var shirt = (Bill_Production)NewBill("BasicShirt");
            shirt.repeatCount = 4;
            A.billStack.AddBill(shirt);

            foreach (var bench in new[] { A, B, C })
            {
                var cloth = ThingMaker.MakeThing(ThingDefOf.Cloth);
                cloth.stackCount = 200;
                GenPlace.TryPlaceThing(cloth, bench.InteractionCell, Map, ThingPlaceMode.Near);
            }

            var tailoring = DefDatabase<WorkTypeDef>.GetNamed("Tailoring");
            int crafters = 0;
            foreach (var pawn in Map.mapPawns.FreeColonists)
            {
                if (pawn.WorkTypeIsDisabled(tailoring))
                    continue;
                pawn.workSettings.EnableAndInitialize();
                foreach (var work in DefDatabase<WorkTypeDef>.AllDefs)
                {
                    if (!pawn.WorkTypeIsDisabled(work))
                        pawn.workSettings.SetPriority(work, work == tailoring ? 1 : 0);
                }
                pawn.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                crafters++;
            }
            Log.Message($"[WCTEST] crafters={crafters}");
            Check(crafters >= 2, $"at least two colonists can tailor ({crafters})");

            craftStartTick = Find.TickManager.TicksGame;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
        });
        Wait(60);
        Step("craft", Poll);
    }

    private static void CountCompletion(Bill_Production __instance)
    {
        if (__instance.recipe.defName == "Make_Apparel_BasicShirt")
            completions++;
    }

    private static void Poll()
    {
        int working = Map.mapPawns.FreeColonists.Count(pawn =>
            pawn.CurJob?.def == JobDefOf.DoBill && pawn.CurJob.bill?.recipe.defName == "Make_Apparel_BasicShirt");
        maxConcurrent = Math.Max(maxConcurrent, working);

        bool finished = new[] { A, B, C }.All(bench => ((Bill_Production)bench.billStack[0]).repeatCount == 0) && working == 0;
        int elapsed = Find.TickManager.TicksGame - craftStartTick;
        if (!finished && elapsed < 3 * GenDate.TicksPerDay)
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
            wait = 60;
            steps.Enqueue(("craft", Poll));
            return;
        }

        Log.Message($"[WCTEST] crafting took {elapsed} ticks, completions={completions}, maxConcurrent={maxConcurrent}");
        Check(finished, "all repetitions done within three days");
        Check(completions == 4, $"exactly four shirts made ({completions})");
        Check(maxConcurrent >= 2, $"several benches worked at once (max {maxConcurrent})");
        Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        Finish();
    }

    // ---------------------------------------------------------------- helpers

    private static Building_WorkTable Spawn(ThingDef def, IntVec3 cell)
    {
        var bench = (Building_WorkTable)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
        bench.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(bench, cell, Map, Rot4.North, WipeMode.Vanish);
        return bench;
    }

    private static Building_WorkTable Bench(int id) =>
        Map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver)
            .OfType<Building_WorkTable>()
            .First(bench => bench.thingIDNumber == id);

    private static Bill NewBill(string apparel) => DefDatabase<RecipeDef>.GetNamed("Make_Apparel_" + apparel).MakeNewBill();

    private static WorkbenchGroup? GroupOf(Building_WorkTable bench) => (WorkbenchGroup?)registryOf.Invoke(null, new object[] { bench });

    private static string Recipes(Building_WorkTable bench) =>
        string.Join(",", bench.billStack.Bills.Select(bill => bill.recipe.defName.Replace("Make_Apparel_", "")));

    private static void ExpectAll(string expected, string what)
    {
        foreach (var bench in new[] { A, B, C })
            Check(Recipes(bench) == expected, $"{what}: {bench} = {Recipes(bench)}");
    }

    private static void CheckUniqueLoadIds(string when)
    {
        var ids = new[] { A, B, C }.SelectMany(bench => bench.billStack.Bills).Select(bill => bill.GetUniqueLoadID()).ToList();
        Check(ids.Distinct().Count() == ids.Count && ids.All(id => !id.EndsWith("_-1")), $"bill load IDs unique {when}");
    }

    // Copy settings right away instead of waiting for the frame check.
    private static void BillCopyAll(Bill bill) =>
        AccessTools.Method(typeof(WorkbenchGroup), "SyncSettings", new[] { typeof(Bill) }).Invoke(GroupOf((Building_WorkTable)bill.billStack.billGiver), new object[] { bill });

    private static Command_Action? GizmoOrNull(Building_WorkTable bench, string key, params Building_WorkTable[] select)
    {
        Find.Selector.ClearSelection();
        foreach (var thing in select)
            Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false);
        string label = key.Translate();
        return bench.GetGizmos().OfType<Command_Action>().FirstOrDefault(command => command.defaultLabel == label);
    }

    private static Command_Action Gizmo(Building_WorkTable bench, string key, params Building_WorkTable[] select) =>
        GizmoOrNull(bench, key, select) ?? throw new Exception($"no gizmo {key} on {bench}");

    private static void Click(Building_WorkTable bench, string key, params Building_WorkTable[] select)
    {
        var command = Gizmo(bench, key, select);
        if (command.Disabled)
            throw new Exception($"{key} disabled: {command.disabledReason}");
        command.action();
    }

    private static void Reload(string name)
    {
        leaving = Current.Game;
        planned = false;
        wait = 120;
        GameDataSaveLoader.LoadGame(name);
    }

    /// <summary>Rewrites a save into the pre-2.0 layout: shared bills in the group, empty member stacks, IDs on the benches.</summary>
    private static void WriteLegacySave(string from, string to)
    {
        var doc = new XmlDocument();
        doc.Load(GenFilePaths.FilePathForSavedGame(from));
        var manager = doc.SelectSingleNode("//li[@Class='WorkbenchConnect.Core.WorkbenchGroupManager']")
            ?? throw new Exception("manager node not found");

        int groups = 0;
        foreach (XmlNode group in manager.SelectNodes("groups/li")!)
        {
            string id = group["loadID"]!.InnerText;
            XmlElement? savedBills = null;
            foreach (XmlNode member in group.SelectNodes("members/li")!)
            {
                string thingId = member.InnerText.StartsWith("Thing_") ? member.InnerText.Substring(6) : member.InnerText;
                var thing = doc.SelectSingleNode($"//thing[id='{thingId}']") ?? doc.SelectSingleNode($"//li[id='{thingId}']")
                    ?? throw new Exception($"thing {thingId} not found");
                var bills = thing.SelectSingleNode("billStack/bills") ?? throw new Exception("bills not found");
                if (savedBills == null)
                {
                    savedBills = doc.CreateElement("savedBills");
                    foreach (XmlNode li in bills.ChildNodes)
                        savedBills.AppendChild(li.CloneNode(true));
                }
                while (bills.FirstChild != null)
                    bills.RemoveChild(bills.FirstChild);

                string number = new string(thingId.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                var groupId = doc.CreateElement("workbenchGroupID_" + number);
                groupId.InnerText = id;
                thing.AppendChild(groupId);
            }

            group.RemoveChild(group["members"]!);
            group.RemoveChild(group["linkedBills"]!);
            var label = doc.CreateElement("groupLabel");
            label.InnerText = "Workbench Group " + id;
            group.AppendChild(label);
            group.AppendChild(savedBills!);
            groups++;
        }

        doc.Save(GenFilePaths.FilePathForSavedGame(to));
        Log.Message($"[WCTEST] wrote legacy save with {groups} groups");
    }

    private static void Check(bool ok, string what)
    {
        if (ok)
        {
            passes++;
            Log.Message("[WCTEST] PASS " + what);
        }
        else
        {
            Fail(what);
        }
    }

    private static void Fail(string what)
    {
        failures++;
        Log.Error("[WCTEST] FAIL " + what);
    }

    private static void Finish()
    {
        Log.Message($"[WCTEST] DONE passes={passes} failures={failures}");
        Root.Shutdown();
    }
}
