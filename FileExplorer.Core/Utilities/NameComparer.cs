using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FileExplorer.Core.Utilities;

public sealed class NameComparer : IComparer<string>
{
    private static readonly Regex _splitRegex = new(@"\d+|\D+", RegexOptions.Compiled);

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        if (x == ".." && y != "..") return -1;
        if (y == ".." && x != "..") return 1;

        bool isHiddenX = x.StartsWith('.') && x != "." && x != "..";
        bool isHiddenY = y.StartsWith('.') && y != "." && y != "..";
        if (isHiddenX != isHiddenY)
        {
            return isHiddenX ? 1 : -1;
        }

        SplitNameAndExtension(x, out string stemX, out string extX);
        SplitNameAndExtension(y, out string stemY, out string extY);

        int stemCmp = NaturalCompare(stemX, stemY);
        if (stemCmp != 0)
        {
            return stemCmp;
        }

        int extCmp = NaturalCompare(extX, extY);
        if (extCmp != 0)
        {
            return extCmp;
        }

        return string.Compare(x, y, StringComparison.Ordinal);
    }

    private static void SplitNameAndExtension(string fileName, out string stem, out string extension)
    {
        if (fileName == "..")
        {
            stem = fileName;
            extension = string.Empty;
            return;
        }

        int lastDotIndex = fileName.LastIndexOf('.');
        if (lastDotIndex <= 0)
        {
            stem = fileName;
            extension = string.Empty;
            return;
        }

        stem = fileName.Substring(0, lastDotIndex);
        extension = fileName.Substring(lastDotIndex);
    }

    private static int NaturalCompare(string x, string y)
    {
        MatchCollection xParts = _splitRegex.Matches(x);
        MatchCollection yParts = _splitRegex.Matches(y);

        int count = Math.Min(xParts.Count, yParts.Count);

        for (int i = 0; i < count; i++)
        {
            string a = xParts[i].Value;
            string b = yParts[i].Value;

            bool aIsNum = char.IsDigit(a[0]);
            bool bIsNum = char.IsDigit(b[0]);

            if (aIsNum && bIsNum)
            {
                int numCmp = CompareNumericChunks(a, b);
                if (numCmp != 0)
                {
                    return numCmp;
                }
            }
            else
            {
                int strCmp = string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
                if (strCmp != 0)
                {
                    return strCmp;
                }
            }
        }

        return xParts.Count.CompareTo(yParts.Count);
    }

    private static int CompareNumericChunks(string a, string b)
    {
        string aTrimmed = a.TrimStart('0');
        string bTrimmed = b.TrimStart('0');

        if (aTrimmed.Length != bTrimmed.Length)
        {
            return aTrimmed.Length.CompareTo(bTrimmed.Length);
        }

        int cmp = string.Compare(aTrimmed, bTrimmed, StringComparison.Ordinal);
        if (cmp != 0)
        {
            return cmp;
        }

        return a.Length.CompareTo(b.Length);
    }
}
