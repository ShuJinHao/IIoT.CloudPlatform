using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

#if CLOUD_ANALYZER
namespace IIoT.CloudPlatform.Analyzers;
#else
namespace IIoT.SharedKernel.Architecture;
#endif

#if CLOUD_ANALYZER
internal static class ReadOnlySqlGuard
#else
public static class ReadOnlySqlGuard
#endif
{
    private static readonly Regex ReadOnlySqlStart = new(
        @"^(SELECT|WITH)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SqlSelectKeyword = new(
        @"\bSELECT\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SqlWriteOrDdlKeyword = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|UPSERT|REPLACE|INTO|CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|CALL|EXEC|EXECUTE|COPY|VACUUM|ANALYZE|LOCK|COMMENT|REINDEX|CLUSTER|REFRESH)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SqlFunctionCall = new(
        "(?<![\\w$\\\"])(?<name>(?:[A-Za-z_][A-Za-z0-9_$]*|\\\"[^\\\"]*\\\")(?:\\s*\\.\\s*(?:[A-Za-z_][A-Za-z0-9_$]*|\\\"[^\\\"]*\\\"))?)\\s*\\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex QualifiedColumnCast = new(
        @"^\s*(?:[A-Za-z_][A-Za-z0-9_$]*\s*\.\s*)?[A-Za-z_][A-Za-z0-9_$]*\s*::\s*(?<type>text|integer|timestamptz)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex MakeIntervalArguments = new(
        @"^\s*hours\s*=>\s*(?:[A-Za-z_][A-Za-z0-9_$]*\s*\.\s*)?[A-Za-z_][A-Za-z0-9_$]*\s*::\s*integer\s*,\s*mins\s*=>\s*(?:[A-Za-z_][A-Za-z0-9_$]*\s*\.\s*)?[A-Za-z_][A-Za-z0-9_$]*\s*::\s*integer\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> SqlStructuralParentheses = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "all",
        "and",
        "any",
        "as",
        "coalesce",
        "exists",
        "extract",
        "filter",
        "from",
        "greatest",
        "in",
        "join",
        "least",
        "nullif",
        "or",
        "over",
        "trim",
        "values"
    };

    public static string Require(string sql)
    {
        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        if (!IsReadOnly(sql))
        {
            throw new InvalidOperationException(
                "SQL text is not proven to be read-only.");
        }

        return sql;
    }

    internal static bool IsReadOnly(string sql)
    {
        if (!TryGetSqlCode(sql, out var code))
        {
            return false;
        }

        code = code.Trim();
        if (code.Length == 0)
        {
            return false;
        }

        var semicolon = code.IndexOf(';');
        if (semicolon >= 0)
        {
            if (semicolon != code.Length - 1 ||
                code.IndexOf(';', semicolon + 1) >= 0)
            {
                return false;
            }

            code = code.Substring(0, code.Length - 1).TrimEnd();
        }

        if (!ReadOnlySqlStart.IsMatch(code) ||
            SqlWriteOrDdlKeyword.IsMatch(code) ||
            ContainsUnprovenSqlFunction(code))
        {
            return false;
        }

        return !code.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) ||
               SqlSelectKeyword.IsMatch(code);
    }

    private static bool ContainsUnprovenSqlFunction(string code)
    {
        foreach (Match match in SqlFunctionCall.Matches(code))
        {
            var name = Regex.Replace(
                match.Groups["name"].Value,
                @"\s+",
                string.Empty);
            if (SqlStructuralParentheses.Contains(name))
            {
                continue;
            }

            if (!TryGetFunctionArguments(
                    code,
                    match.Index + match.Length,
                    out var arguments) ||
                !HasProvenPgCatalogSignature(name, arguments))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasProvenPgCatalogSignature(
        string qualifiedName,
        string arguments)
    {
        const string prefix = "pg_catalog.";
        if (!qualifiedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            qualifiedName.IndexOf('"') >= 0)
        {
            return false;
        }

        var functionName = qualifiedName.Substring(prefix.Length);
        if (functionName.Equals("count", StringComparison.OrdinalIgnoreCase))
        {
            return arguments.Trim() == "*";
        }

        if (functionName.Equals("make_interval", StringComparison.OrdinalIgnoreCase))
        {
            return MakeIntervalArguments.IsMatch(arguments);
        }

        var argument = QualifiedColumnCast.Match(arguments);
        if (!argument.Success)
        {
            return false;
        }

        var castType = argument.Groups["type"].Value;
        return (functionName.Equals("upper", StringComparison.OrdinalIgnoreCase) &&
                castType.Equals("text", StringComparison.OrdinalIgnoreCase)) ||
               (functionName.Equals("min", StringComparison.OrdinalIgnoreCase) &&
                castType.Equals("text", StringComparison.OrdinalIgnoreCase)) ||
               (functionName.Equals("sum", StringComparison.OrdinalIgnoreCase) &&
                castType.Equals("integer", StringComparison.OrdinalIgnoreCase)) ||
               (functionName.Equals("max", StringComparison.OrdinalIgnoreCase) &&
                castType.Equals("timestamptz", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetFunctionArguments(
        string code,
        int argumentStart,
        out string arguments)
    {
        var depth = 1;
        for (var index = argumentStart; index < code.Length; index++)
        {
            if (code[index] == '(')
            {
                depth++;
            }
            else if (code[index] == ')' && --depth == 0)
            {
                arguments = code.Substring(argumentStart, index - argumentStart);
                return true;
            }
        }

        arguments = string.Empty;
        return false;
    }

    private static bool TryGetSqlCode(string sql, out string code)
    {
        var builder = new StringBuilder(sql.Length);
        var state = SqlLexicalState.Code;
        for (var index = 0; index < sql.Length; index++)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';
            switch (state)
            {
                case SqlLexicalState.Code:
                    if (current == '-' && next == '-')
                    {
                        builder.Append("  ");
                        index++;
                        state = SqlLexicalState.LineComment;
                    }
                    else if (current == '/' && next == '*')
                    {
                        builder.Append("  ");
                        index++;
                        state = SqlLexicalState.BlockComment;
                    }
                    else if (current == '\'')
                    {
                        builder.Append(' ');
                        state = SqlLexicalState.StringLiteral;
                    }
                    else if (current == '"')
                    {
                        builder.Append('"');
                        state = SqlLexicalState.QuotedIdentifier;
                    }
                    else if (current == '[')
                    {
                        builder.Append(' ');
                        state = SqlLexicalState.BracketIdentifier;
                    }
                    else
                    {
                        builder.Append(current);
                    }
                    break;
                case SqlLexicalState.LineComment:
                    builder.Append(current is '\r' or '\n' ? current : ' ');
                    if (current is '\r' or '\n')
                    {
                        state = SqlLexicalState.Code;
                    }
                    break;
                case SqlLexicalState.BlockComment:
                    if (current == '*' && next == '/')
                    {
                        builder.Append("  ");
                        index++;
                        state = SqlLexicalState.Code;
                    }
                    else
                    {
                        builder.Append(current is '\r' or '\n' ? current : ' ');
                    }
                    break;
                case SqlLexicalState.StringLiteral:
                    builder.Append(' ');
                    if (current == '\'' && next == '\'')
                    {
                        builder.Append(' ');
                        index++;
                    }
                    else if (current == '\'')
                    {
                        state = SqlLexicalState.Code;
                    }
                    break;
                case SqlLexicalState.QuotedIdentifier:
                    if (current == '"' && next == '"')
                    {
                        builder.Append("qq");
                        index++;
                    }
                    else if (current == '"')
                    {
                        builder.Append('"');
                        state = SqlLexicalState.Code;
                    }
                    else
                    {
                        builder.Append(current is '\r' or '\n' ? current : 'q');
                    }
                    break;
                case SqlLexicalState.BracketIdentifier:
                    builder.Append(' ');
                    if (current == ']' && next == ']')
                    {
                        builder.Append(' ');
                        index++;
                    }
                    else if (current == ']')
                    {
                        state = SqlLexicalState.Code;
                    }
                    break;
            }
        }

        code = builder.ToString();
        return state is SqlLexicalState.Code or SqlLexicalState.LineComment;
    }

    private enum SqlLexicalState
    {
        Code,
        LineComment,
        BlockComment,
        StringLiteral,
        QuotedIdentifier,
        BracketIdentifier
    }
}
