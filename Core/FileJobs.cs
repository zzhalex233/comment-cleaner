using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CommentCleanerWpf.Core;

public sealed class FileJobs
{
    public record Options(
        bool DryRun,
        bool Backup,
        bool ClearCommentOnlyLines,
        bool DeleteClearedLines
    );

    public record Result(
        int Total,
        int Changed,
        int Failed,
        Dictionary<string, string> UnifiedDiffs,
        Dictionary<string, List<DiffUtil.SideRow>> SideBySide
    );

    public static HashSet<string> ParseSuffixes(string raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return set;

        var parts = raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p0 in parts)
        {
            var p = p0.Trim();
            if (p.Length == 0) continue;
            if (!p.StartsWith(".")) p = "." + p;
            set.Add(p.ToLowerInvariant());
        }
        return set;
    }

    public static List<string> CollectFilesBySuffix(string root, HashSet<string> suffixes, bool ignoreHidden)
    {
        var files = new List<string>();

        foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            try
            {
                if (ignoreHidden)
                {
                    var attr = File.GetAttributes(f);
                    if ((attr & FileAttributes.Hidden) != 0) continue;
                }

                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (suffixes.Contains(ext))
                    files.Add(f);
            }
            catch
            {
            }
        }

        return files;
    }

    public Result ProcessFiles(
        List<string> targets,
        string? workspaceFolder,
        Options opt,
        Action<string> log)
    {
        int changed = 0;
        int failed = 0;

        var diffs = new Dictionary<string, string>();
        var side = new Dictionary<string, List<DiffUtil.SideRow>>();

        string? backupRoot = null;
        string? backupBase = null;

        if (opt.Backup)
        {
            if (!string.IsNullOrWhiteSpace(workspaceFolder) && Directory.Exists(workspaceFolder))
            {
                backupBase = workspaceFolder!;
                backupRoot = Path.Combine(workspaceFolder!, "_comment_cleaner_backup");
            }
            else
            {
                var common = Path.GetDirectoryName(targets[0]) ?? Directory.GetCurrentDirectory();
                backupBase = common;
                backupRoot = Path.Combine(common, "_comment_cleaner_backup");
            }

            Directory.CreateDirectory(backupRoot);
            log($"[Backup] {backupRoot}");
        }

        foreach (var path in targets)
        {
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var src = ReadTextSmart(path);

                var dst = StripByExt(src, ext);

                if (opt.ClearCommentOnlyLines)
                    dst = ClearOrDeleteCommentOnlyLines(src, dst, opt.DeleteClearedLines);

                if (!string.Equals(dst, src, StringComparison.Ordinal))
                {
                    changed++;
                    log($"[Change] {path}");

                    if (opt.DryRun)
                    {
                        diffs[path] = DiffUtil.UnifiedDiff(src, dst, path);

                        side[path] = DiffUtil.SideBySideRows(src, dst);
                    }
                    else
                    {
                        if (opt.Backup && backupRoot is not null && backupBase is not null)
                            BackupOne(path, backupRoot, backupBase);

                        File.WriteAllText(path, dst, new UTF8Encoding(false));
                    }
                }
                else
                {
                    log($"[NoChange] {path}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                log($"[Fail] {path}  {ex.Message}");
            }
        }

        return new Result(targets.Count, changed, failed, diffs, side);
    }

    private static string StripByExt(string src, string ext)
    {
        if (ext is ".java" or ".c" or ".h" or ".cpp" or ".cc" or ".cxx" or ".hpp" or ".hh" or ".hxx"
            or ".cs" or ".js" or ".jsx" or ".ts" or ".tsx" or ".go" or ".rs" or ".kt" or ".kts" or ".swift")
            return CommentStripper.StripCStyle(src);

        if (ext == ".py")
            return CommentStripper.StripPython(src);

        return src;
    }

    private static string ReadTextSmart(string path)
    {
        try { return File.ReadAllText(path, new UTF8Encoding(false, true)); } catch { }
        try { return File.ReadAllText(path, Encoding.Default); } catch { }
        return File.ReadAllText(path, Encoding.Latin1);
    }

    private static void BackupOne(string path, string backupRoot, string baseDir)
    {
        var rel = Path.GetRelativePath(baseDir, path);
        var dst = Path.Combine(backupRoot, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(path, dst, overwrite: true);
    }

    private static string ClearOrDeleteCommentOnlyLines(string original, string cleaned, bool deleteLines)
    {
        var oLines = SplitLinesKeepEnd(original);
        var cLines = SplitLinesKeepEnd(cleaned);

        int m = Math.Max(oLines.Count, cLines.Count);
        while (oLines.Count < m) oLines.Add("\n");
        while (cLines.Count < m) cLines.Add("\n");

        var sb = new StringBuilder(cleaned.Length);

        for (int i = 0; i < m; i++)
        {
            var o = oLines[i];
            var c = cLines[i];

            bool oBlank = IsBlankLine(o);
            bool cBlank = IsBlankLine(c);

            if (oBlank)
            {
                sb.Append(c);
                continue;
            }

            if (cBlank)
            {
                if (deleteLines) continue;

                if (o.EndsWith("\r\n")) sb.Append("\r\n");
                else if (o.EndsWith("\n")) sb.Append("\n");
                else sb.Append("");
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsBlankLine(string line)
    {
        var t = line.Replace("\r", "").Replace("\n", "");
        return string.IsNullOrWhiteSpace(t);
    }

    private static List<string> SplitLinesKeepEnd(string s)
    {
        var list = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            int j = s.IndexOf('\n', i);
            if (j == -1)
            {
                list.Add(s.Substring(i));
                break;
            }
            list.Add(s.Substring(i, j - i + 1));
            i = j + 1;
        }
        if (s.Length == 0) list.Add("");
        return list;
    }
}