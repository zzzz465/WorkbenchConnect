using HarmonyLib;
using UnityEngine;
using Verse;

namespace WorkbenchConnect;

public class WorkbenchConnectMod : Mod
{
    public static WorkbenchConnectSettings Settings { get; private set; } = null!;

    public WorkbenchConnectMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<WorkbenchConnectSettings>();
        new Harmony("jungooji.workbenchconnect").PatchAll(typeof(WorkbenchConnectMod).Assembly);
    }

    public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);

    public override string SettingsCategory() => "Workbench Connect";
}

public class WorkbenchConnectSettings : ModSettings
{
    public bool enableDebugLogging;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref enableDebugLogging, "enableDebugLogging", false);
        base.ExposeData();
    }

    public void DoWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.CheckboxLabeled("WorkbenchConnect.Settings.DebugLogging".Translate(), ref enableDebugLogging);
        listing.End();
    }
}

internal static class DebugLog
{
    private const string Prefix = "[WorkbenchConnect] ";

    public static bool Enabled => WorkbenchConnectMod.Settings?.enableDebugLogging == true;

    public static void Message(string text)
    {
        if (Enabled)
            Log.Message(Prefix + text);
    }
}
