using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using WorkbenchConnect.Utils;

namespace WorkbenchConnect.Core
{
    public static class WorkbenchGroupUtility
    {
        public static readonly Texture2D LinkTex = ContentFinder<Texture2D>.Get("UI/Commands/SetTargetFuelLevel");
        public static readonly Texture2D UnlinkTex = ContentFinder<Texture2D>.Get("UI/Commands/Break");
        public static readonly Texture2D SelectLinkedTex = ContentFinder<Texture2D>.Get("UI/Commands/SelectMonument");

        public static IEnumerable<Gizmo> WorkbenchGroupMemberGizmos(IWorkbenchGroupMember member)
        {
            if (member?.Map == null)
                yield break;

            var manager = member.Map.GetComponent<WorkbenchGroupManager>();
            if (manager == null)
                yield break;

            // Link/Unlink commands
            if (member.Group == null)
            {
                yield return new Command_Action
                {
                    action = () => StartLinkingProcess(member),
                    defaultLabel = "WorkbenchConnect.LinkWorkbench".Translate(),
                    defaultDesc = "WorkbenchConnect.LinkWorkbenchDesc".Translate(),
                    icon = LinkTex,
                    hotKey = KeyBindingDefOf.Misc1
                };
            }
            else
            {
                yield return new Command_Action
                {
                    action = () => member.Group.RemoveMember(member),
                    defaultLabel = "WorkbenchConnect.UnlinkWorkbench".Translate(),
                    defaultDesc = "WorkbenchConnect.UnlinkWorkbenchDesc".Translate(),
                    icon = UnlinkTex,
                    hotKey = KeyBindingDefOf.Misc2
                };

                if (member.Group.members.Count > 1)
                {
                    yield return new Command_Action
                    {
                        action = () => SelectLinkedWorkbenches(member),
                        defaultLabel = "WorkbenchConnect.SelectLinked".Translate(),
                        defaultDesc = "WorkbenchConnect.SelectLinkedDesc".Translate(),
                        icon = SelectLinkedTex,
                        hotKey = KeyBindingDefOf.Misc3
                    };
                }
            }
        }

        private static void StartLinkingProcess(IWorkbenchGroupMember initiator)
        {
            var targetingParams = new TargetingParameters
            {
                canTargetBuildings = true,
                canTargetPawns = false,
                canTargetItems = false,
                validator = target => IsValidLinkTarget(initiator, (LocalTargetInfo)target)
            };

            Find.Targeter.BeginTargeting(targetingParams, target => LinkWorkbenches(initiator, target), null, null, null);
        }

        private static bool IsValidLinkTarget(IWorkbenchGroupMember initiator, LocalTargetInfo target)
        {
            if (!(target.Thing is IWorkbenchGroupMember targetMember))
                return false;

            if (targetMember == initiator)
                return false;

            if (targetMember.Map != initiator.Map)
                return false;

            if (!string.Equals(targetMember.WorkbenchGroupTag, initiator.WorkbenchGroupTag))
                return false;

            float distance = initiator.Position.DistanceTo(targetMember.Position);
            if (distance > WorkbenchConnectMod.settings.maxConnectionDistance)
                return false;

            return true;
        }

        private static void LinkWorkbenches(IWorkbenchGroupMember initiator, LocalTargetInfo target)
        {
            if (!(target.Thing is IWorkbenchGroupMember targetMember))
                return;

            DebugHelper.Log($"Linking workbenches: {initiator} and {targetMember}");

            var manager = initiator.Map.GetComponent<WorkbenchGroupManager>();
            if (manager == null)
                return;

            WorkbenchGroup group;

            if (initiator.Group != null && targetMember.Group != null)
            {
                // Both have groups - merge them
                if (initiator.Group == targetMember.Group)
                    return; // Already in same group

                group = initiator.Group;
                var targetGroup = targetMember.Group;
                
                // Move all members from target group to initiator group
                var membersToMove = targetGroup.members.ToList();
                foreach (var member in membersToMove)
                {
                    targetGroup.RemoveMember(member);
                    group.AddMember(member);
                }
            }
            else if (initiator.Group != null)
            {
                // Initiator has group, add target to it
                group = initiator.Group;
                group.AddMember(targetMember);
            }
            else if (targetMember.Group != null)
            {
                // Target has group, add initiator to it
                group = targetMember.Group;
                group.AddMember(initiator);
            }
            else
            {
                // Neither has group, create new one
                group = manager.NewGroup(initiator);
                group.AddMember(targetMember);
            }

            Messages.Message("WorkbenchConnect.WorkbenchesLinked".Translate(), MessageTypeDefOf.PositiveEvent);
        }

        private static void SelectLinkedWorkbenches(IWorkbenchGroupMember member)
        {
            if (member.Group == null)
                return;

            var selectables = member.Group.members
                .Where(m => m is Thing thing && thing.Spawned)
                .Cast<Thing>()
                .ToList();

            if (selectables.Any())
            {
                Find.Selector.ClearSelection();
                Find.Selector.ClearSelection();
                foreach (var selectable in selectables)
                {
                    Find.Selector.Select(selectable, false, true);
                }
            }
        }

        public static void DrawSelectionOverlaysFor(IWorkbenchGroupMember member)
        {
            if (member?.Group == null || !member.DrawConnectionOverlay)
                return;

            foreach (var otherMember in member.Group.members)
            {
                if (otherMember == member || !(otherMember is Thing thing) || !thing.Spawned)
                    continue;

                GenDraw.DrawLineBetween(
                    member.Position.ToVector3Shifted(),
                    otherMember.Position.ToVector3Shifted(),
                    SimpleColor.Yellow
                );
            }
        }

        public static void SetWorkbenchGroup(IWorkbenchGroupMember member, WorkbenchGroup group)
        {
            if (member.Group == group)
                return;

            member.Group?.RemoveMember(member);
            
            if (group != null)
            {
                group.AddMember(member);
            }
        }

        public static bool CanFormGroupWith(IWorkbenchGroupMember a, IWorkbenchGroupMember b)
        {
            if (a == null || b == null || a == b)
                return false;

            if (a.Map != b.Map)
                return false;

            if (!string.Equals(a.WorkbenchGroupTag, b.WorkbenchGroupTag))
                return false;

            float distance = a.Position.DistanceTo(b.Position);
            return distance <= WorkbenchConnectMod.settings.maxConnectionDistance;
        }
    }
}