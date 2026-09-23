using EngineeringMcp.Sql;

namespace EngineeringMcp.Tests.Sql;

public class SqlValidatorTests
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("select * from services")]
    [InlineData("SELECT * FROM services;")]
    [InlineData("SELECT * FROM services ;  \n")]
    [InlineData("-- leading comment\nSELECT id FROM services")]
    [InlineData("SELECT /* ; DROP TABLE x */ 1")]
    [InlineData("SELECT 1 -- ; DELETE FROM services")]
    [InlineData("WITH recent AS (SELECT * FROM deployments) SELECT * FROM recent")]
    [InlineData("WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n WHERE x < 5) SELECT x FROM n")]
    [InlineData("(SELECT 1) UNION (SELECT 2)")]
    [InlineData("SELECT 'DROP TABLE services; --' AS s")]
    [InlineData("SELECT 'it''s; fine' AS s")]
    [InlineData("SELECT \"update\", \"delete\" FROM audit")]
    [InlineData("SELECT updated_at, deleted, inserted_by, created_on FROM services")]
    [InlineData("SELECT replace(name, '-', '_') FROM services")]
    [InlineData("SELECT count(*) FROM incidents WHERE severity = ? AND service_id = :id")]
    [InlineData("SELECT [order], `group` FROM t")]
    [InlineData("SELECT name FROM pragma_table_info('services')")]
    [InlineData("SELECT s.name, COUNT(d.id) FROM services s LEFT JOIN deployments d ON d.service_id = s.id GROUP BY s.name ORDER BY 2 DESC LIMIT 5")]
    public void Accepts_read_only_sqlite_queries(string sql)
    {
        var result = SqlValidator.Validate(sql, SqlDialect.Sqlite);

        Assert.True(result.IsValid, result.Error);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("SELECT $$; DROP TABLE services; $$ AS body")]
    [InlineData("SELECT $tag$ DELETE FROM x $tag$")]
    [InlineData("SELECT E'it\\'s; DELETE' AS s")]
    [InlineData("SELECT * FROM services WHERE id = $1")]
    [InlineData("SELECT now() - interval '1 day'")]
    [InlineData("SELECT 'a' || \"Name\" FROM services")]
    public void Accepts_read_only_postgres_queries(string sql)
    {
        var result = SqlValidator.Validate(sql, SqlDialect.PostgreSql);

        Assert.True(result.IsValid, result.Error);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   \n\t ", "empty")]
    [InlineData("-- just a comment", "only comments")]
    [InlineData("/* nothing */", "only comments")]
    [InlineData(";", "empty")]
    [InlineData("DELETE FROM services", "Only SELECT")]
    [InlineData("UPDATE services SET name = 'x'", "Only SELECT")]
    [InlineData("INSERT INTO services (name) VALUES ('x')", "Only SELECT")]
    [InlineData("DROP TABLE services", "Only SELECT")]
    [InlineData("CREATE TABLE t (id INT)", "Only SELECT")]
    [InlineData("PRAGMA table_info(services)", "Only SELECT")]
    [InlineData("ATTACH DATABASE 'other.db' AS other", "Only SELECT")]
    [InlineData("VACUUM", "Only SELECT")]
    [InlineData("EXPLAIN SELECT 1", "Only SELECT")]
    [InlineData("VALUES (1)", "Only SELECT")]
    [InlineData("BEGIN; SELECT 1", "single statement")]
    [InlineData("SELECT 1; SELECT 2", "single statement")]
    [InlineData("SELECT 1; DROP TABLE services", "single statement")]
    [InlineData("SELECT 1;;", "single statement")]
    [InlineData("SELECT 1; -- trailing comment hides nothing\nDELETE FROM services", "single statement")]
    [InlineData("SELECT * INTO backup FROM services", "INTO")]
    [InlineData("WITH gone AS (DELETE FROM services RETURNING *) SELECT * FROM gone", "DELETE")]
    [InlineData("WITH x AS (UPDATE services SET name = 'a' RETURNING id) SELECT * FROM x", "UPDATE")]
    [InlineData("SELECT * FROM services FOR UPDATE", "UPDATE")]
    [InlineData("SELECT * FROM services FOR SHARE", "Row-locking")]
    [InlineData("SELECT * FROM services FOR KEY SHARE", "Row-locking")]
    [InlineData("SELECT load_extension('evil.so')", "load_extension")]
    [InlineData("SELECT writefile('/tmp/x', 'data')", "writefile")]
    [InlineData("SELECT 'unterminated", "Unterminated string")]
    [InlineData("SELECT \"unterminated", "Unterminated quoted identifier")]
    [InlineData("SELECT [unterminated", "Unterminated quoted identifier")]
    [InlineData("SELECT /* unterminated", "Unterminated block comment")]
    [InlineData("/* outer /* inner */ DELETE FROM services; */ SELECT 1", "Nested block comments")]
    [InlineData("SELECT $a$; DELETE FROM services; $a$", "single statement")]
    public void Rejects_unsafe_sqlite_queries(string sql, string expectedReason)
    {
        var result = SqlValidator.Validate(sql, SqlDialect.Sqlite);

        Assert.False(result.IsValid);
        Assert.Contains(expectedReason, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, result.Statement);
    }

    [Theory]
    [InlineData("SELECT pg_sleep(30)", "pg_sleep")]
    [InlineData("SELECT pg_catalog.pg_sleep(30)", "pg_sleep")]
    [InlineData("SELECT pg_read_file('/etc/passwd')", "pg_read_file")]
    [InlineData("SELECT pg_advisory_lock(1)", "pg_advisory_lock")]
    [InlineData("SELECT nextval('services_id_seq')", "nextval")]
    [InlineData("SELECT set_config('statement_timeout', '0', false)", "set_config")]
    [InlineData("SELECT * FROM dblink('host=x', 'DELETE FROM t') AS t(a int)", "dblink")]
    [InlineData("SELECT query_to_xml('DELETE FROM services', true, true, '')", "query_to_xml")]
    [InlineData("SELECT $$ unterminated", "Unterminated dollar-quoted")]
    [InlineData("SELECT E'unterminated \\'", "Unterminated string")]
    [InlineData("SELECT * FROM services FOR NO KEY UPDATE", "Row-locking")]
    public void Rejects_unsafe_postgres_queries(string sql, string expectedReason)
    {
        var result = SqlValidator.Validate(sql, SqlDialect.PostgreSql);

        Assert.False(result.IsValid);
        Assert.Contains(expectedReason, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dollar_quoting_is_postgres_only()
    {
        // In SQLite `$a` is a parameter name, so the semicolons below are real statement breaks.
        const string sql = "SELECT $a$; SELECT 2; $a$";

        Assert.True(SqlValidator.Validate(sql, SqlDialect.PostgreSql).IsValid);
        Assert.False(SqlValidator.Validate(sql, SqlDialect.Sqlite).IsValid);
    }

    [Fact]
    public void Brackets_are_identifiers_only_in_sqlite()
    {
        const string sql = "SELECT [x; DELETE FROM t]";

        Assert.True(SqlValidator.Validate(sql, SqlDialect.Sqlite).IsValid);
        Assert.False(SqlValidator.Validate(sql, SqlDialect.PostgreSql).IsValid);
    }

    [Fact]
    public void Trailing_semicolon_is_removed_from_the_statement()
    {
        var result = SqlValidator.Validate("  SELECT 1 ;  ", SqlDialect.Sqlite);

        Assert.True(result.IsValid);
        Assert.Equal("SELECT 1", result.Statement);
    }

    [Fact]
    public void Statement_text_is_preserved_including_comments()
    {
        var result = SqlValidator.Validate("SELECT 1 /* why */", SqlDialect.Sqlite);

        Assert.Equal("SELECT 1 /* why */", result.Statement);
    }

    [Fact]
    public void Rejects_statements_over_the_length_limit()
    {
        var sql = "SELECT " + new string('1', SqlValidator.MaxStatementLength);

        var result = SqlValidator.Validate(sql, SqlDialect.Sqlite);

        Assert.False(result.IsValid);
        Assert.Contains("longer than", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.False(SqlValidator.Validate(null, SqlDialect.Sqlite).IsValid);
    }
}
