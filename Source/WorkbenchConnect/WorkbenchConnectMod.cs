using HarmonyLib;
using Verse;

namespace WorkbenchConnect
{
    [StaticConstructorOnStartup]
    public class WorkbenchConnectMod : Mod
    {
        public static WorkbenchConnectSettings settings;

        public WorkbenchConnectMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<WorkbenchConnectSettings>();
            
            var harmony = new Harmony("workbenchconnect.mod");
            harmony.PatchAll();
            
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