using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using WorkbenchConnect.Patches;
using WorkbenchConnect.Utils;

namespace WorkbenchConnect.Core
{
    [StaticConstructorOnStartup]
    public static class WorkbenchGroupUtility
    {
        public static readonly Texture2D LinkTex = ContentFinder<Texture2D>.Get("UI/Commands/LinkStorageSettings");
        public static readonly Texture2D UnlinkTex = ContentFinder<Texture2D>.Get("UI/Commands/UnlinkStorageSettings");
        public static readonly Texture2D SelectLinkedTex = ContentFinder<Texture2D>.Get("UI/Commands/SelectAllLinked");
        
        public static readonly Material GroupedMat = MaterialPool.MatFrom("UI/Overlays/SelectionBracket", ShaderDatabase.MetaOverlay, new Color(0f, 1f, 1f, 0.2f));

        private static List<IWorkbenchGroupMember> tmpMembers = new List<IWorkbenchGroupMember>();
        private static HashSet<WorkbenchGroup> drawnGroupsThisFrame = new HashSet<WorkbenchGroup>();

        public static IEnumerable<Gizmo> WorkbenchGroupMemberGizmos(IWorkbenchGroupMember member)
        {
            if (member?.Map == null)
                yield break;

            var manager = member.Map.GetComponent<WorkbenchGroupManager>();
            if (manager == null)
                yield break;

            bool allInSameGroup = true;
            WorkbenchGroup workbenchGroup = null;
            tmpMembers.Clear();

            // Collect all selected workbenches with same type
            foreach (object selectedObject in Find.Selector.SelectedObjects)
            {
                // Check if it's a Building_WorkTable and get its member data
                if (!(selectedObject is Building_WorkTable workTable))
                {
                    continue;
                }
                
                var workbenchMember = Building_WorkTable_Patches.GetMemberData(workTable);
                
                if (workbenchMember.WorkbenchGroupTag != member.WorkbenchGroupTag)
                {
                    continue;
                }
                
                if (workbenchMember.Map != member.Map)
                {
                    continue;
                }
                
                tmpMembers.Add(workbenchMember);
                
                if (allInSameGroup)
                {
                    if (workbenchGroup == null && workbenchMember.Group != null)
                    {
                        workbenchGroup = workbenchMember.Group;
                    }
                    if (workbenchGroup != workbenchMember.Group)
                    {
                        allInSameGroup = false;
                    }
                }
            }

            if (workbenchGroup == null)
            {
                allInSameGroup = false;
            }

            // Link workbenches command
            Command_Action linkCommand = new Command_Action
            {
                defaultLabel = "WorkbenchConnect.LinkWorkbench".Translate(),
                defaultDesc = "WorkbenchConnect.LinkWorkbenchDesc".Translate(),
                icon = LinkTex,
                action = () =>
                {
                    // Find existing group or create new one
                    WorkbenchGroup group = tmpMembers.FirstOrDefault(m => m.Group != null)?.Group;
                    
                    if (group == null)
                    {
                        // Collect all bills from all selected workbenches before creating group
                        var allBills = new List<Bill>();
                        foreach (var tmpMember in tmpMembers)
                        {
                            if (tmpMember.BillStack?.Bills?.Any() == true)
                            {
                                allBills.AddRange(tmpMember.BillStack.Bills);
                            }
                        }
                        
                        // Create new group with collected bills
                        group = manager.NewGroupWithBills(tmpMembers[0], allBills);
                    }
                    
                    foreach (IWorkbenchGroupMember tmpMember in tmpMembers)
                    {
                        SetWorkbenchGroup(tmpMember, group);
                    }
                    
                    if (tmpMembers.Count > 1)
                    {
                        Messages.Message("WorkbenchConnect.WorkbenchesLinked".Translate(tmpMembers.Count), null, MessageTypeDefOf.NeutralEvent, historical: false);
                    }
                    else
                    {
                        Messages.Message("WorkbenchConnect.WorkbenchLinked".Translate(), null, MessageTypeDefOf.NeutralEvent, historical: false);
                    }
                },
                hotKey = KeyBindingDefOf.Misc1
            };

            if (tmpMembers.Count < 2)
            {
                linkCommand.Disable("WorkbenchConnect.LinkDisabledSelectTwo".Translate());
            }
            else if (allInSameGroup)
            {
                linkCommand.Disable("WorkbenchConnect.AlreadyLinked".Translate());
            }

            yield return linkCommand;

            // Unlink command (only if member is in a group)
            if (member.Group != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "WorkbenchConnect.UnlinkWorkbench".Translate(),
                    defaultDesc = "WorkbenchConnect.UnlinkWorkbenchDesc".Translate(),
                    icon = UnlinkTex,
                    action = () =>
                    {
                        member.Group.RemoveMember(member);
                        
                        if (tmpMembers.Count > 1)
                        {
                            Messages.Message("WorkbenchConnect.WorkbenchesUnlinked".Translate(tmpMembers.Count), null, MessageTypeDefOf.NeutralEvent, historical: false);
                        }
                        else
                        {
                            Messages.Message("WorkbenchConnect.WorkbenchUnlinked".Translate(), null, MessageTypeDefOf.NeutralEvent, historical: false);
                        }
                    },
                    hotKey = KeyBindingDefOf.Misc2
                };

                // Select all linked command
                yield return new Command_Action
                {
                    defaultLabel = "WorkbenchConnect.SelectLinked".Translate(),
                    defaultDesc = "WorkbenchConnect.SelectLinkedDesc".Translate(),
                    icon = SelectLinkedTex,
                    action = () =>
                    {
                        bool playSound = false;
                        foreach (IWorkbenchGroupMember groupMember in member.Group.members)
                        {
                            var selectableThing = groupMember.SelectableThing;
                            if (selectableThing != null && !Find.Selector.IsSelected(selectableThing))
                            {
                                Find.Selector.Select(selectableThing, playSound: false);
                                playSound = true;
                            }
                        }
                        if (playSound)
                        {
                            SoundDefOf.ThingSelected.PlayOneShotOnCamera();
                        }
                    },
                    hotKey = KeyBindingDefOf.Misc3
                };
            }
        }



        public static void DrawSelectionOverlaysFor(IWorkbenchGroupMember member)
        {
            if (member?.Group == null)
                return;

            // Simple frame tracking - clear if we're starting fresh this frame
            if (drawnGroupsThisFrame.Count == 0 || Event.current.type == EventType.Repaint)
            {
                if (Event.current.type == EventType.Repaint)
                    drawnGroupsThisFrame.Clear();
            }

            if (drawnGroupsThisFrame.Contains(member.Group))
                return;

            foreach (var otherMember in member.Group.members)
            {
                if (otherMember.DrawConnectionOverlay)
                {
                    var selectableThing = otherMember.SelectableThing;
                    if (selectableThing != null)
                    {
                        SelectionDrawer.DrawSelectionBracketFor(selectableThing, GroupedMat);
                    }
                }
            }
            drawnGroupsThisFrame.Add(member.Group);
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
    }
}
