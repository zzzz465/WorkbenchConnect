using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using WorkbenchConnect.Core;

namespace WorkbenchConnect.UI;

[StaticConstructorOnStartup]
internal static class GroupGizmos
{
    private static readonly Texture2D LinkTex = ContentFinder<Texture2D>.Get("UI/Commands/LinkStorageSettings");
    private static readonly Texture2D UnlinkTex = ContentFinder<Texture2D>.Get("UI/Commands/UnlinkStorageSettings");
    private static readonly Texture2D SelectLinkedTex = ContentFinder<Texture2D>.Get("UI/Commands/SelectAllLinked");

    public static IEnumerable<Gizmo> For(Building_WorkTable bench)
    {
        var manager = WorkbenchGroupManager.For(bench.Map);
        if (manager == null)
            yield break;

        var group = GroupRegistry.Of(bench);
        var selection = SelectedLike(bench);

        // With several benches selected the game merges these gizmos and runs each action,
        // so the actions must be safe to repeat.
        var link = new Command_Action
        {
            defaultLabel = "WorkbenchConnect.LinkWorkbench".Translate(),
            defaultDesc = "WorkbenchConnect.LinkWorkbenchDesc".Translate(),
            icon = LinkTex,
            hotKey = KeyBindingDefOf.Misc1,
            action = () => Link(manager, selection),
        };
        if (selection.Count < 2)
            link.Disable("WorkbenchConnect.LinkDisabledSelectTwo".Translate());
        else if (group != null && selection.All(other => GroupRegistry.Of(other) == group))
            link.Disable("WorkbenchConnect.AlreadyLinked".Translate());
        yield return link;

        if (group == null)
            yield break;

        yield return new Command_Action
        {
            defaultLabel = "WorkbenchConnect.UnlinkWorkbench".Translate(),
            defaultDesc = "WorkbenchConnect.UnlinkWorkbenchDesc".Translate(),
            icon = UnlinkTex,
            hotKey = KeyBindingDefOf.Misc2,
            action = () => Unlink(bench),
        };

        yield return new Command_Action
        {
            defaultLabel = "WorkbenchConnect.SelectLinked".Translate(),
            defaultDesc = "WorkbenchConnect.SelectLinkedDesc".Translate(),
            icon = SelectLinkedTex,
            hotKey = KeyBindingDefOf.Misc3,
            action = () => SelectLinked(bench),
        };
    }

    private static List<Building_WorkTable> SelectedLike(Building_WorkTable bench)
    {
        var benches = new List<Building_WorkTable>();
        foreach (var selected in Find.Selector.SelectedObjectsListForReading)
        {
            if (selected is Building_WorkTable other && other.def == bench.def && other.Map == bench.Map
                && other.Spawned && other.Faction == Faction.OfPlayer)
            {
                benches.Add(other);
            }
        }
        return benches;
    }

    /// <summary>
    /// Unlinked benches pool their bills into a new group. Benches joining an existing group
    /// replace their bills with the group's.
    /// </summary>
    private static void Link(WorkbenchGroupManager manager, List<Building_WorkTable> selection)
    {
        var benches = selection.Where(bench => bench.Spawned && bench.Map == manager.map).ToList();
        if (benches.Count < 2)
            return;

        var group = benches.Select(GroupRegistry.Of).FirstOrDefault(found => found != null);
        if (group == null)
        {
            manager.Link(benches);
        }
        else
        {
            var joining = benches.Where(bench => GroupRegistry.Of(bench) != group).ToList();
            if (joining.Count == 0)
                return;

            foreach (var bench in joining)
            {
                GroupRegistry.Of(bench)?.Leave(bench);
                group.Join(bench);
            }
        }

        Messages.Message("WorkbenchConnect.WorkbenchLinked".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
    }

    private static void Unlink(Building_WorkTable bench)
    {
        var group = GroupRegistry.Of(bench);
        if (group == null)
            return;

        group.Leave(bench);
        Messages.Message("WorkbenchConnect.WorkbenchUnlinked".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
    }

    private static void SelectLinked(Building_WorkTable bench)
    {
        var group = GroupRegistry.Of(bench);
        if (group == null)
            return;

        bool selectedAny = false;
        foreach (var member in group.Members)
        {
            if (!Find.Selector.IsSelected(member))
            {
                Find.Selector.Select(member, playSound: false);
                selectedAny = true;
            }
        }

        if (selectedAny)
            SoundDefOf.ThingSelected.PlayOneShotOnCamera();
    }
}

[StaticConstructorOnStartup]
internal static class GroupOverlay
{
    private static readonly Material LinkedMat =
        MaterialPool.MatFrom("UI/Overlays/SelectionBracket", ShaderDatabase.MetaOverlay, new Color(0f, 1f, 1f, 0.2f));

    /// <summary>Brackets every member of a selected bench's group, once per frame per group.</summary>
    public static void Draw(Building_WorkTable bench)
    {
        var group = GroupRegistry.Of(bench);
        if (group == null || group.overlayFrame == Time.frameCount)
            return;

        group.overlayFrame = Time.frameCount;
        foreach (var member in group.Members)
            SelectionDrawer.DrawSelectionBracketFor(member, LinkedMat);
    }
}
