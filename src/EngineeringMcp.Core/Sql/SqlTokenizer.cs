namespace EngineeringMcp.Sql;

internal enum SqlTokenKind
{
    Word,
    QuotedIdentifier,
    StringLiteral,
    Number,
    Parameter,
    Semicolon,
    Symbol,
}

internal readonly record struct SqlToken(SqlTokenKind Kind, string Text, int Position)
{
    public bool IsWord(string word) =>
        Kind == SqlTokenKind.Word && string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);

    public bool IsSymbol(char symbol) => Kind == SqlTokenKind.Symbol && Text.Length == 1 && Text[0] == symbol;
}

internal sealed class SqlTokenizeException(string message, int position) : Exception(message)
{
    public int Position { get; } = position;
}

/// <summary>
/// A small lexer that understands just enough SQL to tell keywords apart from text that only
/// looks like keywords: string literals, quoted identifiers and comments. Comments are dropped.
/// </summary>
/// <remarks>
/// Lexing is dialect-aware because the two engines disagree on what counts as a string. A
/// PostgreSQL dollar-quoted body is a single literal, while SQLite would read the same characters
/// as parameters and statements. Anything that the two engines could read differently, such as a
/// block comment that opens another block comment, is rejected rather than guessed at.
/// </remarks>
internal sealed class SqlTokenizer
{
    private readonly string _sql;
    private readonly SqlDialect _dialect;
    private readonly List<SqlToken> _tokens = [];
    private int _pos;

    private SqlTokenizer(string sql, SqlDialect dialect)
    {
        _sql = sql;
        _dialect = dialect;
    }

    public static IReadOnlyList<SqlToken> Tokenize(string sql, SqlDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var tokenizer = new SqlTokenizer(sql, dialect);
        tokenizer.Run();
        return tokenizer._tokens;
    }

    private char Current => _sql[_pos];

    private char Peek(int offset = 1) => _pos + offset < _sql.Length ? _sql[_pos + offset] : '\0';

    private void Run()
    {
        while (_pos < _sql.Length)
        {
            var c = Current;

            if (char.IsWhiteSpace(c))
            {
                _pos++;
            }
            else if (c == '-' && Peek() == '-')
            {
                SkipLineComment();
            }
            else if (c == '/' && Peek() == '*')
            {
                SkipBlockComment();
            }
            else if (c == '\'')
            {
                ReadQuoted('\'', SqlTokenKind.StringLiteral, backslashEscapes: false);
            }
            else if ((c == 'E' || c == 'e') && Peek() == '\'' && _dialect == SqlDialect.PostgreSql)
            {
                _pos++;
                ReadQuoted('\'', SqlTokenKind.StringLiteral, backslashEscapes: true, start: _pos - 1);
            }
            else if (c == '"')
            {
                ReadQuoted('"', SqlTokenKind.QuotedIdentifier, backslashEscapes: false);
            }
            else if (c == '`' && _dialect == SqlDialect.Sqlite)
            {
                ReadQuoted('`', SqlTokenKind.QuotedIdentifier, backslashEscapes: false);
            }
            else if (c == '[' && _dialect == SqlDialect.Sqlite)
            {
                ReadBracketIdentifier();
            }
            else if (c == '$')
            {
                ReadDollar();
            }
            else if (c == ';')
            {
                Add(SqlTokenKind.Semicolon, _pos, 1);
            }
            else if (IsWordStart(c))
            {
                ReadWhile(SqlTokenKind.Word, IsWordPart);
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(Peek())))
            {
                ReadWhile(SqlTokenKind.Number, ch => char.IsAsciiLetterOrDigit(ch) || ch == '.');
            }
            else if ((c == '?' || c == ':' || c == '@') && IsWordStart(Peek()) && _dialect == SqlDialect.Sqlite)
            {
                var start = _pos++;
                while (_pos < _sql.Length && IsWordPart(Current))
                {
                    _pos++;
                }

                _tokens.Add(new SqlToken(SqlTokenKind.Parameter, _sql[start.._pos], start));
            }
            else
            {
                Add(SqlTokenKind.Symbol, _pos, 1);
            }
        }
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private void Add(SqlTokenKind kind, int start, int length)
    {
        _tokens.Add(new SqlToken(kind, _sql.Substring(start, length), start));
        _pos = start + length;
    }

    private void ReadWhile(SqlTokenKind kind, Func<char, bool> predicate)
    {
        var start = _pos;
        while (_pos < _sql.Length && predicate(Current))
        {
            _pos++;
        }

        _tokens.Add(new SqlToken(kind, _sql[start.._pos], start));
    }

    private void SkipLineComment()
    {
        while (_pos < _sql.Length && Current != '\n')
        {
            _pos++;
        }
    }

    private void SkipBlockComment()
    {
        var start = _pos;
        _pos += 2;
        while (_pos < _sql.Length)
        {
            if (Current == '*' && Peek() == '/')
            {
                _pos += 2;
                return;
            }

            if (Current == '/' && Peek() == '*')
            {
                // PostgreSQL nests block comments and SQLite does not, so the two would disagree
                // about where this comment ends.
                throw new SqlTokenizeException("Nested block comments are not allowed.", _pos);
            }

            _pos++;
        }

        throw new SqlTokenizeException("Unterminated block comment.", start);
    }

    private void ReadQuoted(char quote, SqlTokenKind kind, bool backslashEscapes, int? start = null)
    {
        var tokenStart = start ?? _pos;
        _pos++;
        while (_pos < _sql.Length)
        {
            var c = Current;
            if (backslashEscapes && c == '\\')
            {
                _pos += 2;
                continue;
            }

            if (c == quote)
            {
                if (Peek() == quote)
                {
                    _pos += 2;
                    continue;
                }

                _pos++;
                _tokens.Add(new SqlToken(kind, _sql[tokenStart.._pos], tokenStart));
                return;
            }

            _pos++;
        }

        var what = kind == SqlTokenKind.StringLiteral ? "string literal" : "quoted identifier";
        throw new SqlTokenizeException($"Unterminated {what}.", tokenStart);
    }

    private void ReadBracketIdentifier()
    {
        var start = _pos;
        var end = _sql.IndexOf(']', _pos + 1);
        if (end < 0)
        {
            throw new SqlTokenizeException("Unterminated quoted identifier.", start);
        }

        Add(SqlTokenKind.QuotedIdentifier, start, end - start + 1);
    }

    private void ReadDollar()
    {
        var start = _pos;

        if (_dialect == SqlDialect.PostgreSql)
        {
            if (char.IsAsciiDigit(Peek()))
            {
                // Positional parameter: $1, $2, ...
                _pos++;
                while (_pos < _sql.Length && char.IsAsciiDigit(Current))
                {
                    _pos++;
                }

                _tokens.Add(new SqlToken(SqlTokenKind.Parameter, _sql[start.._pos], start));
                return;
            }

            // A dollar-quote tag: $$ or $tag$ where tag is an identifier without dollars.
            var tagEnd = _pos + 1;
            while (tagEnd < _sql.Length && (char.IsLetterOrDigit(_sql[tagEnd]) || _sql[tagEnd] == '_'))
            {
                tagEnd++;
            }

            if (tagEnd < _sql.Length && _sql[tagEnd] == '$')
            {
                var tag = _sql[start..(tagEnd + 1)];
                var close = _sql.IndexOf(tag, tagEnd + 1, StringComparison.Ordinal);
                if (close < 0)
                {
                    throw new SqlTokenizeException("Unterminated dollar-quoted string.", start);
                }

                Add(SqlTokenKind.StringLiteral, start, close + tag.Length - start);
                return;
            }

            Add(SqlTokenKind.Symbol, start, 1);
            return;
        }

        // SQLite: $name is a named parameter.
        _pos++;
        while (_pos < _sql.Length && (char.IsLetterOrDigit(Current) || Current == '_'))
        {
            _pos++;
        }

        _tokens.Add(new SqlToken(SqlTokenKind.Parameter, _sql[start.._pos], start));
    }
}
