namespace PlcInfo.Services;

/// <summary>
/// Kind of an alignment step between two sequences.
/// </summary>
public enum AlignKind
{
    /// <summary>The items are identical.</summary>
    Equal,
    /// <summary>The item exists only in the old sequence.</summary>
    Delete,
    /// <summary>The item exists only in the new sequence.</summary>
    Insert,
    /// <summary>The items differ but are similar enough to be one changed item.</summary>
    Modify
}

/// <summary>
/// One alignment step. <see cref="OldIndex"/> is -1 for inserts, <see cref="NewIndex"/> is -1 for deletes.
/// </summary>
public readonly record struct AlignStep(AlignKind Kind, int OldIndex, int NewIndex);

/// <summary>
/// Aligns two sequences (rungs, networks, source lines) so that an inserted or
/// removed item does not make every following item look changed.
/// </summary>
public static class SequenceAligner
{
    // Above this many DP cells, the unmatched middle is treated as replaced wholesale
    // (keeps memory bounded on pathological inputs; typical edits are far smaller
    // after common prefix/suffix trimming).
    private const long MaxCells = 25_000_000;

    /// <summary>
    /// Computes a longest-common-subsequence alignment of <paramref name="oldKeys"/> and
    /// <paramref name="newKeys"/>. When <paramref name="similarity"/> is given, deleted and
    /// inserted items within the same gap are paired in order as <see cref="AlignKind.Modify"/>
    /// when their similarity (0..1) reaches <paramref name="threshold"/>.
    /// </summary>
    public static IReadOnlyList<AlignStep> Align(
        IReadOnlyList<string> oldKeys,
        IReadOnlyList<string> newKeys,
        Func<int, int, double>? similarity = null,
        double threshold = 0.5)
    {
        var steps = Lcs(oldKeys, newKeys);
        return similarity == null ? steps : PairGaps(steps, similarity, threshold);
    }

    private static List<AlignStep> Lcs(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var steps = new List<AlignStep>(Math.Max(a.Count, b.Count));

        // Common prefix and suffix are equal; only the middle needs dynamic programming.
        int prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix])
            prefix++;
        int suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix &&
               a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix])
            suffix++;

        for (int i = 0; i < prefix; i++)
            steps.Add(new AlignStep(AlignKind.Equal, i, i));

        int n = a.Count - prefix - suffix;
        int m = b.Count - prefix - suffix;

        if ((long)(n + 1) * (m + 1) > MaxCells)
        {
            for (int i = 0; i < n; i++)
                steps.Add(new AlignStep(AlignKind.Delete, prefix + i, -1));
            for (int j = 0; j < m; j++)
                steps.Add(new AlignStep(AlignKind.Insert, -1, prefix + j));
        }
        else
        {
            // lengths[i, j] = LCS length of a[prefix+i..] and b[prefix+j..]
            var lengths = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lengths[i, j] = a[prefix + i] == b[prefix + j]
                        ? lengths[i + 1, j + 1] + 1
                        : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[prefix + x] == b[prefix + y])
                {
                    steps.Add(new AlignStep(AlignKind.Equal, prefix + x, prefix + y));
                    x++;
                    y++;
                }
                else if (lengths[x + 1, y] >= lengths[x, y + 1])
                {
                    steps.Add(new AlignStep(AlignKind.Delete, prefix + x, -1));
                    x++;
                }
                else
                {
                    steps.Add(new AlignStep(AlignKind.Insert, -1, prefix + y));
                    y++;
                }
            }
            for (; x < n; x++)
                steps.Add(new AlignStep(AlignKind.Delete, prefix + x, -1));
            for (; y < m; y++)
                steps.Add(new AlignStep(AlignKind.Insert, -1, prefix + y));
        }

        for (int i = suffix; i > 0; i--)
            steps.Add(new AlignStep(AlignKind.Equal, a.Count - i, b.Count - i));

        return steps;
    }

    /// <summary>
    /// Within each run of non-equal steps, pairs deletes with inserts (keeping order)
    /// when they are similar, turning them into modifications.
    /// </summary>
    private static List<AlignStep> PairGaps(List<AlignStep> steps, Func<int, int, double> similarity, double threshold)
    {
        var result = new List<AlignStep>(steps.Count);
        int index = 0;
        while (index < steps.Count)
        {
            if (steps[index].Kind == AlignKind.Equal)
            {
                result.Add(steps[index++]);
                continue;
            }

            var deletes = new List<int>();
            var inserts = new List<int>();
            while (index < steps.Count && steps[index].Kind != AlignKind.Equal)
            {
                if (steps[index].Kind == AlignKind.Delete)
                    deletes.Add(steps[index].OldIndex);
                else
                    inserts.Add(steps[index].NewIndex);
                index++;
            }

            // Order-preserving greedy pairing: each delete takes the first remaining
            // similar insert after the previously paired one.
            var pairedInsertFor = new Dictionary<int, int>();
            int nextInsert = 0;
            foreach (var oldIndex in deletes)
            {
                for (int k = nextInsert; k < inserts.Count; k++)
                {
                    if (similarity(oldIndex, inserts[k]) >= threshold)
                    {
                        pairedInsertFor[oldIndex] = k;
                        nextInsert = k + 1;
                        break;
                    }
                }
            }

            // Emit in new-sequence order, with unpaired deletes before the next paired item.
            int insertCursor = 0;
            foreach (var oldIndex in deletes)
            {
                if (!pairedInsertFor.TryGetValue(oldIndex, out var k))
                {
                    result.Add(new AlignStep(AlignKind.Delete, oldIndex, -1));
                    continue;
                }
                for (; insertCursor < k; insertCursor++)
                    result.Add(new AlignStep(AlignKind.Insert, -1, inserts[insertCursor]));
                result.Add(new AlignStep(AlignKind.Modify, oldIndex, inserts[k]));
                insertCursor = k + 1;
            }
            for (; insertCursor < inserts.Count; insertCursor++)
                result.Add(new AlignStep(AlignKind.Insert, -1, inserts[insertCursor]));
        }
        return result;
    }

    /// <summary>
    /// Jaccard similarity of two token multisets (0 when both are empty).
    /// </summary>
    public static double Jaccard(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
            return 0;
        var countsA = a.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        var countsB = b.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        int intersection = countsA.Sum(kv => Math.Min(kv.Value, countsB.GetValueOrDefault(kv.Key)));
        int union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }
}
