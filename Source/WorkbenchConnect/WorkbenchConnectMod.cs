using HarmonyLib;
using Verse;
using WorkbenchConnect.Patches;

namespace WorkbenchConnect
{
    [StaticConstructorOnStartup]
    public class WorkbenchConnectMod : Mod
    {
        public static WorkbenchConnectSettings settings;

        public WorkbenchConnectMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<WorkbenchConnectSettings>();
            
            var harmony = new Harmony("jungooji.workbenchconnect");
            Building_WorkTable_Patches.Patch(harmony);
            WorkGiver_DoBill_Patches.Patch(harmony);
            Bill_Patches.Patch(harmony);
            UnfinishedThing_Patches.Patch(harmony);
            
            Log.Message("[WorkbenchConnect] Mod loaded successfully");
        }

        public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
        {
            settings.DoWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "WorkbenchConnect";
        }
    }

    public class WorkbenchConnectSettings : ModSettings
    {
        public bool enableDebugLogging = false;
        public float maxConnectionDistance = 10f;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableDebugLogging, "enableDebugLogging", false);
            Scribe_Values.Look(ref maxConnectionDistance, "maxConnectionDistance", 10f);
            base.ExposeData();
        }

        public void DoWindowContents(UnityEngine.Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            listingStandard.CheckboxLabeled("Enable debug logging", ref enableDebugLogging);
            listingStandard.Label($"Max connection distance: {maxConnectionDistance:F1}");
            maxConnectionDistance = listingStandard.Slider(maxConnectionDistance, 1f, 50f);

            listingStandard.End();
        }
    }
}
