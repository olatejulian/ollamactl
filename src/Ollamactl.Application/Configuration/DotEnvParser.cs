namespace Ollamactl.Application.Configuration;

public static class DotEnvParser
{
    public static IReadOnlyDictionary<string, string> Parse(string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(contents);

        for (var lineNumber = 1; ; lineNumber++)
        {
            var rawLine = reader.ReadLine();
            if (rawLine is null)
            {
                break;
            }

            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new FormatException($"Invalid environment entry at line {lineNumber}.");
            }

            var key = line[..separator].Trim();
            if (!IsValidKey(key))
            {
                throw new FormatException($"Invalid environment variable name at line {lineNumber}.");
            }

            values[key] = ParseValue(line[(separator + 1)..].Trim(), lineNumber);
        }

        return values;
    }

    private static bool IsValidKey(string key)
    {
        if (key.Length == 0 || !(char.IsAsciiLetter(key[0]) || key[0] == '_'))
        {
            return false;
        }

        for (var index = 1; index < key.Length; index++)
        {
            if (!(char.IsAsciiLetterOrDigit(key[index]) || key[index] == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static string ParseValue(string value, int lineNumber)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (value[0] is '\'' or '"')
        {
            return ParseQuotedValue(value, lineNumber);
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '#' && (index == 0 || char.IsWhiteSpace(value[index - 1])))
            {
                return value[..index].TrimEnd();
            }
        }

        return value;
    }

    private static string ParseQuotedValue(string value, int lineNumber)
    {
        var quote = value[0];
        var result = new System.Text.StringBuilder(value.Length);
        var escaped = false;

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (quote == '"' && escaped)
            {
                result.Append(character switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => character,
                });
                escaped = false;
                continue;
            }

            if (quote == '"' && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character != quote)
            {
                result.Append(character);
                continue;
            }

            var remainder = value[(index + 1)..].Trim();
            if (remainder.Length > 0 && !remainder.StartsWith('#'))
            {
                throw new FormatException($"Unexpected content after quoted value at line {lineNumber}.");
            }

            return result.ToString();
        }

        throw new FormatException($"Unterminated quoted value at line {lineNumber}.");
    }
}
