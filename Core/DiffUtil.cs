using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace CommentCleanerWpf.Core;

public static class DiffUtil
{
    public enum RowKind { Equal, Deleted, Inserted, Modified }

    public record SideRow(string Left, string Right, RowKind Kind);

    public static string UnifiedDiff(string oldText, string newText, string name)
    {
        var diff = new Differ().CreateLineDiffs(oldText, newText, ignoreWhitespace: false, ignoreCase: false);

        var lines = new List<string>();
        lines.Add($"--- {name} (before)");
        lines.Add($"+++ {name} (after)");

        foreach (var block in diff.DiffBlocks)
        {
            int oldStart = block.DeleteStartA + 1;
            int newStart = block.InsertStartB + 1;
            int oldCount = block.DeleteCountA;
            int newCount = block.InsertCountB;
            lines.Add($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@");

            for (int i = 0; i < oldCount; i++)
            {
                var s = diff.PiecesOld[block.DeleteStartA + i] ?? "";
                lines.Add("-" + s);
            }
            for (int i = 0; i < newCount; i++)
            {
                var s = diff.PiecesNew[block.InsertStartB + i] ?? "";
                lines.Add("+" + s);
            }
        }

        if (lines.Count == 2) lines.Add("(no changes)");
        return string.Join("\n", lines);
    }

    public static List<SideRow> SideBySideRows(string oldText, string newText)
    {
        var builder = new SideBySideDiffBuilder(new Differ());
        var model = builder.BuildDiffModel(oldText, newText);

        int n = Math.Max(model.OldText.Lines.Count, model.NewText.Lines.Count);
        var rows = new List<SideRow>(n);

        for (int i = 0; i < n; i++)
        {
            var ol = i < model.OldText.Lines.Count ? model.OldText.Lines[i] : null;
            var nl = i < model.NewText.Lines.Count ? model.NewText.Lines[i] : null;

            string left = ol?.Text ?? "";
            string right = nl?.Text ?? "";

            RowKind kind = RowKind.Equal;
            if (ol is not null && ol.Type == ChangeType.Deleted) kind = RowKind.Deleted;
            else if (nl is not null && nl.Type == ChangeType.Inserted) kind = RowKind.Inserted;
            else if ((ol is not null && ol.Type == ChangeType.Modified) || (nl is not null && nl.Type == ChangeType.Modified))
                kind = RowKind.Modified;

            rows.Add(new SideRow(left, right, kind));
        }

        return rows;
    }
}