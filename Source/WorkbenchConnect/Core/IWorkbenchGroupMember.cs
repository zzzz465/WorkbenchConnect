using RimWorld;
using Verse;

namespace WorkbenchConnect.Core
{
    public interface IWorkbenchGroupMember
    {
        WorkbenchGroup Group { get; set; }
        Map Map { get; }
        BillStack BillStack { get; }
        string WorkbenchGroupTag { get; }
        bool DrawConnectionOverlay { get; }
        IntVec3 Position { get; }
        
        void Notify_GroupChanged();
    }
}