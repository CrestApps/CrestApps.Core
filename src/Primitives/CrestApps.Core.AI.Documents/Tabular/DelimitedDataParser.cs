namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Parses delimited tabular text (CSV/TSV and similar) into a header row and data rows.
/// Honors RFC 4180-style double-quote quoting, including delimiters and newlines inside
/// quoted fields, so the data can be loaded into a relational table.
/// </summary>
internal static class DelimitedDataParser
{
    private static readonly char[] _candidateDelimiters = [',', '\t', ';', '|'];

    /// <summary>
    /// Parses the provided delimited content.
    /// </summary>
    /// <param name="content">The raw delimited content.</param>
    /// <param name="fileName">The original file name, used to infer the delimiter.</param>
    /// <returns>The parsed header and data rows. Both are empty when no records are found.</returns>
    public static (IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows) Parse(string content, string fileName)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ([], []);
        }

        var delimiter = DetectDelimiter(content, fileName);
        var records = ParseRecords(content, delimiter);

        if (records.Count == 0)
        {
            return ([], []);
        }

        var header = records[0];
        var rows = records.Count > 1 ? records.GetRange(1, records.Count - 1) : [];

        return (header, rows.Cast<IReadOnlyList<string>>().ToList());
    }

    private static char DetectDelimiter(string content, string fileName)
    {
        if (!string.IsNullOrEmpty(fileName) && fileName.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase))
        {
            return '\t';
        }

        var firstLine = GetFirstNonEmptyLine(content);

        if (string.IsNullOrEmpty(firstLine))
        {
            return ',';
        }

        var bestDelimiter = ',';
        var bestCount = -1;

        foreach (var candidate in _candidateDelimiters)
        {
            var count = CountUnquoted(firstLine, candidate);

            if (count > bestCount)
            {
                bestCount = count;
                bestDelimiter = candidate;
            }
        }

        return bestDelimiter;
    }

    private static string GetFirstNonEmptyLine(string content)
    {
        var inQuotes = false;
        var start = 0;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if ((c == '\n' || c == '\r') && !inQuotes)
            {
                var line = content[start..i];

                if (!string.IsNullOrWhiteSpace(line))
                {
                    return line;
                }

                start = i + 1;
            }
        }

        return content[start..];
    }

    private static int CountUnquoted(string line, char delimiter)
    {
        var inQuotes = false;
        var count = 0;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == delimiter && !inQuotes)
            {
                count++;
            }
        }

        return count;
    }

    private static List<List<string>> ParseRecords(string content, char delimiter)
    {
        var records = new List<List<string>>();
        var currentRecord = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
                fieldStarted = true;

                continue;
            }

            if (c == delimiter)
            {
                currentRecord.Add(field.ToString());
                field.Clear();
                fieldStarted = true;

                continue;
            }

            if (c == '\r')
            {
                continue;
            }

            if (c == '\n')
            {
                if (fieldStarted || field.Length > 0 || currentRecord.Count > 0)
                {
                    currentRecord.Add(field.ToString());
                    field.Clear();
                    records.Add(currentRecord);
                    currentRecord = [];
                    fieldStarted = false;
                }

                continue;
            }

            field.Append(c);
            fieldStarted = true;
        }

        if (fieldStarted || field.Length > 0 || currentRecord.Count > 0)
        {
            currentRecord.Add(field.ToString());
            records.Add(currentRecord);
        }

        return records;
    }
}
