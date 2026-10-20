using System;
using System.Collections.Generic;

namespace WorkbenchConnect.Linking;

/// <summary>
/// Bookkeeping for items kept in step across several members. Each row is one linked item and
/// holds one copy per member. The table knows nothing about RimWorld: the caller reads the
/// members' current lists, carries out the returned plan with game calls, and attaches the
/// copies it created.
/// </summary>
public sealed class LinkTable<TMember, TItem>
    where TMember : class
    where TItem : class
{
    public sealed class Row
    {
        internal readonly Dictionary<TMember, TItem> copies = new(ReferenceEqualityComparer.Instance);
        internal readonly Dictionary<TItem, int> stamps = new(ReferenceEqualityComparer.Instance);

        public IReadOnlyDictionary<TMember, TItem> Copies => copies;

        public TItem? CopyFor(TMember member) => copies.TryGetValue(member, out var item) ? item : null;
    }

    public sealed class Plan
    {
        /// <summary>Copies to remove from their member: another member lost its copy of the row.</summary>
        public readonly List<TItem> Delete = new();

        /// <summary>Rows that lack a copy for a member. Clone the source into the target, then <see cref="Attach"/> it.</summary>
        public readonly List<(Row Row, TItem Source, TMember Target)> Clone = new();

        public bool IsEmpty => Delete.Count == 0 && Clone.Count == 0;
    }

    private readonly List<TMember> members = new();
    private readonly List<Row> rows = new();
    private readonly Dictionary<TItem, Row> rowOf = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyList<TMember> Members => members;

    public IReadOnlyList<Row> Rows => rows;

    public bool Contains(TMember member) => members.Contains(member);

    public bool TryGetRow(TItem item, out Row row) => rowOf.TryGetValue(item, out row!);

    public void AddMember(TMember member)
    {
        if (!members.Contains(member))
            members.Add(member);
    }

    /// <summary>Unlinks a member. Its items stay where they are; they just stop being copies of anything.</summary>
    public void RemoveMember(TMember member)
    {
        if (!members.Remove(member))
            return;

        for (int i = rows.Count - 1; i >= 0; i--)
        {
            Detach(rows[i], member);
            if (rows[i].copies.Count == 0)
                rows.RemoveAt(i);
        }
    }

    /// <summary>Adds an already matched row, e.g. one restored from a save.</summary>
    public Row? AddRow(IEnumerable<(TMember Member, TItem Item)> copies)
    {
        var row = new Row();
        foreach (var (member, item) in copies)
        {
            if (members.Contains(member) && !rowOf.ContainsKey(item))
                Attach(row, member, item);
        }

        if (row.copies.Count == 0)
            return null;

        rows.Add(row);
        return row;
    }

    public void Attach(Row row, TMember member, TItem item)
    {
        Detach(row, member);
        row.copies[member] = item;
        rowOf[item] = row;
    }

    /// <summary>
    /// Brings the table in line with the members' lists and returns what the caller must do to
    /// bring the lists in line with each other:
    /// a copy that vanished from its member deletes the row everywhere, an unknown linkable item
    /// becomes a new row, and every row gets a copy for every member.
    /// </summary>
    public Plan Reconcile(Func<TMember, IReadOnlyList<TItem>> listOf, Func<TItem, bool> linkable)
    {
        var plan = new Plan();

        var present = new Dictionary<TMember, HashSet<TItem>>(ReferenceEqualityComparer.Instance);
        foreach (var member in members)
            present[member] = new HashSet<TItem>(listOf(member), ReferenceEqualityComparer.Instance);

        var dead = new HashSet<TItem>(ReferenceEqualityComparer.Instance);
        var gone = new List<TMember>();
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            var row = rows[i];
            gone.Clear();
            foreach (var pair in row.copies)
            {
                if (!present.TryGetValue(pair.Key, out var items) || !items.Contains(pair.Value))
                    gone.Add(pair.Key);
            }

            bool lostCopy = false;
            foreach (var member in gone)
            {
                Detach(row, member);
                lostCopy |= present.ContainsKey(member);
            }

            if (!lostCopy && row.copies.Count > 0)
                continue;

            foreach (var item in row.copies.Values)
            {
                plan.Delete.Add(item);
                dead.Add(item);
                rowOf.Remove(item);
            }
            row.copies.Clear();
            row.stamps.Clear();
            rows.RemoveAt(i);
        }

        foreach (var member in members)
        {
            var list = listOf(member);
            Row? previous = null;
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (rowOf.TryGetValue(item, out var known))
                {
                    previous = known;
                    continue;
                }
                if (dead.Contains(item) || !linkable(item))
                    continue;

                var row = new Row();
                Attach(row, member, item);
                rows.Insert(InsertIndex(previous, list, i), row);
                previous = row;
            }
        }

        foreach (var row in rows)
        {
            TItem? source = null;
            foreach (var item in row.copies.Values)
            {
                source = item;
                break;
            }

            foreach (var member in members)
            {
                if (!row.copies.ContainsKey(member))
                    plan.Clone.Add((row, source!, member));
            }
        }

        return plan;
    }

    /// <summary>Makes the row order follow the order of the linked items in one member's list.</summary>
    public void AdoptOrder(IReadOnlyList<TItem> list)
    {
        var ordered = new List<Row>(rows.Count);
        var seen = new HashSet<Row>(ReferenceEqualityComparer.Instance);
        foreach (var item in list)
        {
            if (rowOf.TryGetValue(item, out var row) && seen.Add(row))
                ordered.Add(row);
        }
        foreach (var row in rows)
        {
            if (seen.Add(row))
                ordered.Add(row);
        }

        rows.Clear();
        rows.AddRange(ordered);
    }

    /// <summary>
    /// Returns the member's list with its linked items in row order, leaving unlinked items in
    /// their slots, or null when the list is already in order or does not match the table.
    /// </summary>
    public List<TItem>? DesiredOrder(TMember member, IReadOnlyList<TItem> list)
    {
        var wanted = new List<TItem>(rows.Count);
        foreach (var row in rows)
        {
            if (row.copies.TryGetValue(member, out var item))
                wanted.Add(item);
        }

        var result = new List<TItem>(list.Count);
        int next = 0;
        bool changed = false;
        foreach (var item in list)
        {
            if (rowOf.TryGetValue(item, out var row) && ReferenceEquals(row.CopyFor(member), item))
            {
                if (next >= wanted.Count)
                    return null;

                var want = wanted[next++];
                changed |= !ReferenceEquals(want, item);
                result.Add(want);
            }
            else
            {
                result.Add(item);
            }
        }

        return changed && next == wanted.Count ? result : null;
    }

    /// <summary>
    /// Returns a copy whose stamp moved since <see cref="Restamp"/>, preferring one that
    /// <paramref name="preferred"/> picks. Copies seen for the first time are only remembered.
    /// </summary>
    public TItem? FindChanged(Row row, Func<TItem, int> stamp, Func<TItem, bool> preferred)
    {
        TItem? changed = null;
        foreach (var item in row.copies.Values)
        {
            int now = stamp(item);
            if (!row.stamps.TryGetValue(item, out int before))
            {
                row.stamps[item] = now;
                continue;
            }
            if (before == now)
                continue;

            if (changed == null || (!preferred(changed) && preferred(item)))
                changed = item;
        }

        return changed;
    }

    public void Restamp(Row row, Func<TItem, int> stamp)
    {
        foreach (var item in row.copies.Values)
            row.stamps[item] = stamp(item);
    }

    private int InsertIndex(Row? previous, IReadOnlyList<TItem> list, int index)
    {
        if (previous != null)
            return rows.IndexOf(previous) + 1;

        for (int i = index + 1; i < list.Count; i++)
        {
            if (rowOf.TryGetValue(list[i], out var next))
                return rows.IndexOf(next);
        }

        return rows.Count;
    }

    private void Detach(Row row, TMember member)
    {
        if (!row.copies.Remove(member, out var item))
            return;

        row.stamps.Remove(item);
        rowOf.Remove(item);
    }
}
