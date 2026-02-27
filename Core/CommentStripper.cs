using System.Text;

namespace CommentCleanerWpf.Core;

public static class CommentStripper
{
    public static string StripCStyle(string text)
    {
        const int NORMAL = 0, IN_STRING = 1, IN_CHAR = 2, IN_LINE = 3, IN_BLOCK = 4;
        int state = NORMAL;
        char quote = '\0';

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            char nxt = (i + 1 < text.Length) ? text[i + 1] : '\0';

            if (state == NORMAL)
            {
                if (ch == '"' )
                {
                    state = IN_STRING; quote = '"';
                    sb.Append(ch);
                    continue;
                }
                if (ch == '\'')
                {
                    state = IN_CHAR; quote = '\'';
                    sb.Append(ch);
                    continue;
                }
                if (ch == '/' && nxt == '/')
                {
                    state = IN_LINE;
                    i++;
                    continue;
                }
                if (ch == '/' && nxt == '*')
                {
                    state = IN_BLOCK;
                    i++;
                    continue;
                }
                sb.Append(ch);
                continue;
            }

            if (state == IN_STRING || state == IN_CHAR)
            {
                sb.Append(ch);
                if (ch == '\\')
                {
                    if (i + 1 < text.Length)
                    {
                        sb.Append(text[i + 1]);
                        i++;
                    }
                    continue;
                }
                if (ch == quote)
                {
                    state = NORMAL;
                }
                continue;
            }

            if (state == IN_LINE)
            {
                if (ch == '\n')
                {
                    sb.Append('\n');
                    state = NORMAL;
                }
                continue;
            }

            if (state == IN_BLOCK)
            {
                if (ch == '*' && nxt == '/')
                {
                    state = NORMAL;
                    i++;
                    continue;
                }
                if (ch == '\n') sb.Append('\n');
                continue;
            }
        }

        return sb.ToString();
    }

    public static string StripPython(string text)
    {

        var noTriple = RemoveStandaloneTripleQuoteBlocks(text);
        return RemoveHashComments(noTriple);
    }

    private static string RemoveHashComments(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool inSingle = false, inDouble = false;
        bool escaped = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];

            if (escaped)
            {
                sb.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                sb.Append(ch);
                escaped = true;
                continue;
            }

            if (!inDouble && ch == '\'' ) { inSingle = !inSingle; sb.Append(ch); continue; }
            if (!inSingle && ch == '"'  ) { inDouble = !inDouble; sb.Append(ch); continue; }

            if (!inSingle && !inDouble && ch == '#')
            {
                while (i < text.Length && text[i] != '\n') i++;
                if (i < text.Length && text[i] == '\n') sb.Append('\n');
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string RemoveStandaloneTripleQuoteBlocks(string text)
    {

        var sb = new StringBuilder(text.Length);
        int i = 0;

        while (i < text.Length)
        {
            int lineStart = i;
            int lineEnd = text.IndexOf('\n', i);
            if (lineEnd == -1) lineEnd = text.Length;

            var line = text.AsSpan(lineStart, lineEnd - lineStart);

            int p = 0;
            while (p < line.Length && (line[p] == ' ' || line[p] == '\t')) p++;

            int prefStart = p;
            int prefCount = 0;
            while (p < line.Length && prefCount < 3)
            {
                char c = line[p];
                if (c is 'r' or 'R' or 'u' or 'U' or 'b' or 'B' or 'f' or 'F')
                {
                    p++;
                    prefCount++;
                    continue;
                }
                break;
            }

            while (p < line.Length && (line[p] == ' ' || line[p] == '\t')) p++;

            string? quote = null;
            if (p + 2 < line.Length && line[p] == '"' && line[p + 1] == '"' && line[p + 2] == '"') quote = "\"\"\"";
            else if (p + 2 < line.Length && line[p] == '\'' && line[p + 1] == '\'' && line[p + 2] == '\'') quote = "'''";

            if (quote is null)
            {
                sb.Append(text, lineStart, (lineEnd - lineStart));
                if (lineEnd < text.Length) sb.Append('\n');
                i = lineEnd + (lineEnd < text.Length ? 1 : 0);
                continue;
            }

            int tripleStartInText = lineStart + p;
            int searchFrom = tripleStartInText + 3;

            int close = text.IndexOf(quote, searchFrom, StringComparison.Ordinal);
            if (close == -1)
            {
                sb.Append(text, lineStart, (lineEnd - lineStart));
                if (lineEnd < text.Length) sb.Append('\n');
                i = lineEnd + (lineEnd < text.Length ? 1 : 0);
                continue;
            }

            int afterClose = close + 3;

            int closeLineEnd = text.IndexOf('\n', close);
            if (closeLineEnd == -1) closeLineEnd = text.Length;

            bool onlyWhitespaceAfterClose = true;
            for (int k = afterClose; k < closeLineEnd; k++)
            {
                char c = text[k];
                if (c != ' ' && c != '\t' && c != '\r')
                {
                    onlyWhitespaceAfterClose = false;
                    break;
                }
            }

            if (!onlyWhitespaceAfterClose)
            {
                sb.Append(text, lineStart, (lineEnd - lineStart));
                if (lineEnd < text.Length) sb.Append('\n');
                i = lineEnd + (lineEnd < text.Length ? 1 : 0);
                continue;
            }

            int blockStart = lineStart;          
            int blockEnd = close;                
            int nlCount = 0;
            for (int k = blockStart; k < afterClose; k++)
                if (text[k] == '\n') nlCount++;

            for (int k = 0; k < nlCount; k++) sb.Append('\n');

            i = closeLineEnd + (closeLineEnd < text.Length ? 1 : 0);
        }

        return sb.ToString();
    }
}