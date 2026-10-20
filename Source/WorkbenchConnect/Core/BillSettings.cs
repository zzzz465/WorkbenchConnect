using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WorkbenchConnect.Core;

/// <summary>
/// The player-facing settings of a bill: the fields <see cref="Bill.Clone"/> and
/// <see cref="Bill_Production.Clone"/> copy. Runtime state such as unfinished things,
/// ingredient search cooldowns and gestation progress stays with each copy.
/// </summary>
internal static class BillSettings
{
    private static readonly AccessTools.FieldRef<Bill, Pawn?> pawnRestriction = AccessTools.FieldRefAccess<Bill, Pawn?>("pawnRestriction");
    private static readonly AccessTools.FieldRef<Bill, bool> slavesOnly = AccessTools.FieldRefAccess<Bill, bool>("slavesOnly");
    private static readonly AccessTools.FieldRef<Bill, bool> mechsOnly = AccessTools.FieldRefAccess<Bill, bool>("mechsOnly");
    private static readonly AccessTools.FieldRef<Bill, bool> nonMechsOnly = AccessTools.FieldRefAccess<Bill, bool>("nonMechsOnly");

    private static readonly AccessTools.FieldRef<Bill_Production, BillStoreModeDef> storeMode = AccessTools.FieldRefAccess<Bill_Production, BillStoreModeDef>("storeMode");
    private static readonly AccessTools.FieldRef<Bill_Production, ISlotGroup?> storeGroup = AccessTools.FieldRefAccess<Bill_Production, ISlotGroup?>("storeGroup");
    private static readonly AccessTools.FieldRef<Bill_Production, ISlotGroup?> includeGroup = AccessTools.FieldRefAccess<Bill_Production, ISlotGroup?>("includeGroup");
    private static readonly AccessTools.FieldRef<Bill_Production, string?> playerCustomName = AccessTools.FieldRefAccess<Bill_Production, string?>("playerCustomName");

    private static readonly AccessTools.FieldRef<ThingFilter, HashSet<ThingDef>> allowedDefs = AccessTools.FieldRefAccess<ThingFilter, HashSet<ThingDef>>("allowedDefs");
    private static readonly AccessTools.FieldRef<ThingFilter, List<SpecialThingFilterDef>> disallowedSpecialFilters = AccessTools.FieldRefAccess<ThingFilter, List<SpecialThingFilterDef>>("disallowedSpecialFilters");
    private static readonly AccessTools.FieldRef<ThingFilter, FloatRange> allowedMentalBreakChance = AccessTools.FieldRefAccess<ThingFilter, FloatRange>("allowedMentalBreakChance");

    /// <summary>A hash of the settings. Allocation free, so it can run every few frames.</summary>
    public static int Stamp(Bill bill)
    {
        var hash = new HashCode();
        hash.Add(bill.precept);
        hash.Add(bill.style);
        hash.Add(bill.globalStyle);
        hash.Add(bill.xenogerm);
        hash.Add(bill.suspended);
        hash.Add(bill.ingredientSearchRadius);
        hash.Add(bill.allowedSkillRange.min);
        hash.Add(bill.allowedSkillRange.max);
        hash.Add(pawnRestriction(bill));
        hash.Add(slavesOnly(bill));
        hash.Add(mechsOnly(bill));
        hash.Add(nonMechsOnly(bill));
        hash.Add(FilterStamp(bill.ingredientFilter));

        if (bill is Bill_Production production)
        {
            hash.Add(production.repeatMode);
            hash.Add(production.repeatCount);
            hash.Add(production.targetCount);
            hash.Add(production.pauseWhenSatisfied);
            hash.Add(production.unpauseWhenYouHave);
            hash.Add(production.includeEquipped);
            hash.Add(production.includeTainted);
            hash.Add(production.hpRange.min);
            hash.Add(production.hpRange.max);
            hash.Add(production.qualityRange.min);
            hash.Add(production.qualityRange.max);
            hash.Add(production.limitToAllowedStuff);
            hash.Add(production.paused);
            hash.Add(storeMode(production));
            hash.Add(storeGroup(production));
            hash.Add(includeGroup(production));
            hash.Add(playerCustomName(production));
        }

        return hash.ToHashCode();
    }

    public static void Copy(Bill from, Bill to)
    {
        to.precept = from.precept;
        to.style = from.style;
        to.globalStyle = from.globalStyle;
        to.xenogerm = from.xenogerm;
        to.suspended = from.suspended;
        to.ingredientSearchRadius = from.ingredientSearchRadius;
        to.allowedSkillRange = from.allowedSkillRange;
        pawnRestriction(to) = pawnRestriction(from);
        slavesOnly(to) = slavesOnly(from);
        mechsOnly(to) = mechsOnly(from);
        nonMechsOnly(to) = nonMechsOnly(from);

        if (from.ingredientFilter != null && to.ingredientFilter != null
            && FilterStamp(from.ingredientFilter) != FilterStamp(to.ingredientFilter))
        {
            to.ingredientFilter.CopyAllowancesFrom(from.ingredientFilter);
        }

        if (from is Bill_Production source && to is Bill_Production target)
        {
            target.repeatMode = source.repeatMode;
            target.repeatCount = source.repeatCount;
            target.targetCount = source.targetCount;
            target.pauseWhenSatisfied = source.pauseWhenSatisfied;
            target.unpauseWhenYouHave = source.unpauseWhenYouHave;
            target.includeEquipped = source.includeEquipped;
            target.includeTainted = source.includeTainted;
            target.hpRange = source.hpRange;
            target.qualityRange = source.qualityRange;
            target.limitToAllowedStuff = source.limitToAllowedStuff;
            target.paused = source.paused;
            storeMode(target) = storeMode(source);
            storeGroup(target) = storeGroup(source);
            includeGroup(target) = includeGroup(source);
            playerCustomName(target) = playerCustomName(source);
        }
    }

    private static int FilterStamp(ThingFilter? filter)
    {
        if (filter == null)
            return 0;

        // Order independent, so two filters with the same allowances match however their sets were built.
        long allowed = 0;
        int count = 0;
        foreach (var def in allowedDefs(filter))
        {
            allowed += Mix(def.shortHash);
            count++;
        }

        long special = 0;
        foreach (var def in disallowedSpecialFilters(filter))
            special += Mix(def.shortHash);

        var hitPoints = filter.AllowedHitPointsPercents;
        var quality = filter.AllowedQualityLevels;
        var mentalBreak = allowedMentalBreakChance(filter);
        return HashCode.Combine(
            HashCode.Combine(allowed, count, special),
            hitPoints.min, hitPoints.max, quality.min, quality.max, mentalBreak.min, mentalBreak.max);
    }

    private static long Mix(ushort value)
    {
        ulong x = (value + 1UL) * 0x9E3779B97F4A7C15UL;
        x ^= x >> 29;
        return (long)x;
    }
}
