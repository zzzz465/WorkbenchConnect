using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WorkbenchConnect.Core;

// Saved as a map component; the full type name is part of the save format.
public class WorkbenchGroupManager : MapComponent
{
    private const int SyncIntervalTicks = 250;
    private const int SettingsIntervalFrames = 10;

    private List<WorkbenchGroup> groups = new();
    private int nextGroupID = 1;

    public WorkbenchGroupManager(Map map) : base(map)
    {
    }

    public static WorkbenchGroupManager? For(Map? map) => map?.GetComponent<WorkbenchGroupManager>();

    internal WorkbenchGroup Link(List<Building_WorkTable> benches)
    {
        var group = new WorkbenchGroup(this, nextGroupID++);
        groups.Add(group);
        group.LinkAll(benches);
        DebugLog.Message($"Group {group.loadID} created with {benches.Count} workbenches");
        return group;
    }

    internal void Remove(WorkbenchGroup group) => groups.Remove(group);

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref nextGroupID, "nextGroupID", 1);
        Scribe_Collections.Look(ref groups, "groups", LookMode.Deep);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            groups ??= new List<WorkbenchGroup>();
            groups.RemoveAll(group => group == null);
            foreach (var group in groups)
                group.manager = this;
            if (groups.Count > 0)
                nextGroupID = Mathf.Max(nextGroupID, groups.Max(group => group.loadID) + 1);
        }
    }

    public override void FinalizeInit()
    {
        base.FinalizeInit();
        LegacySave.Migrate(map, groups);
        foreach (var group in groups.ToList())
            group.Revalidate(map);
    }

    public override void MapComponentTick()
    {
        // Catches bill changes made behind the bill stack's back, e.g. by other mods.
        if (groups.Count == 0 || Find.TickManager.TicksGame % SyncIntervalTicks != 0)
            return;

        foreach (var group in groups.ToList())
            group.Sync();
    }

    public override void MapComponentUpdate()
    {
        // Settings are edited through plain field writes all over the bill UI, so they are
        // compared instead of hooked. Runs while paused too.
        if (groups.Count == 0 || Time.frameCount % SettingsIntervalFrames != 0)
            return;

        foreach (var group in groups)
            group.SyncSettings();
    }
}
