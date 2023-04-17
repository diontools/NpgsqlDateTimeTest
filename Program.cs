var legacy = args.Length > 0;
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", legacy);

var outputFileName = "result.md";
using var output = legacy ? File.AppendText(outputFileName) : File.CreateText(outputFileName);

using var connection = new Npgsql.NpgsqlConnection("Host=localhost;Port=15432;Username=postgres;Password=postgres;");
connection.Open();

connection.CreateTestTables();

output.WriteLine($"## {(legacy ? "旧(v6より前)" : "新(v6とそれ以降)")}");
output.WriteLine("| Input | without time zone | with time zone | date |");
output.WriteLine("| ----- | ----------------- | -------------- | ---- |");

TestPattern[] testPatterns =
{
    new(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
    new(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Local)),
    new(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)),
    new(new DateTime(2000, 1, 1, 21, 0, 0, DateTimeKind.Utc)),
    new(new DateTime(2000, 1, 1, 21, 0, 0, DateTimeKind.Local)),
    new(new DateTime(2000, 1, 1, 21, 0, 0, DateTimeKind.Unspecified)),
};

foreach (var testPattern in testPatterns)
{
    connection.TruncateTestTables();

    Exception? wotzError = null;
    Exception? wtzError = null;
    Exception? dateError = null;
    try { connection.InsertToWithoutTZ(testPattern.Value); } catch (Exception ex) { wotzError = ex; }
    try { connection.InsertToWithTZ(testPattern.Value); } catch (Exception ex) { wtzError = ex; }
    try { connection.InsertToDate(testPattern.Value); } catch (Exception ex) { dateError = ex; }

    var wotz = connection.SelectWithoutTZ();
    var wtz = connection.SelectWithTZ();
    var date = connection.SelectDate();

    static string ToResult(DateTime correct, DateTime? actual, Exception? error)
    {
        if (error != null) return error.GetType().ToString();
        var isNotCorrect = correct != actual;
        var em = isNotCorrect ? "**" : "";
        return $"{em}{actual},{actual?.Kind}{em}";
    }

    output.WriteLine($"| {testPattern.Value},{testPattern.Value.Kind} | {ToResult(testPattern.Value, wotz, wotzError)} | {ToResult(testPattern.Value, wtz, wtzError)} | {ToResult(testPattern.Value, date, dateError)} |");
}

output.WriteLine();


record class TestPattern(DateTime Value);

static class Extensions
{
    public static void CreateTestTables(this Npgsql.NpgsqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TEMPORARY TABLE ts_wotz_test (
                    value timestamp without time zone
                );
                CREATE TEMPORARY TABLE ts_wtz_test (
                    value timestamp with time zone
                );
                CREATE TEMPORARY TABLE date_test (
                    value date
                );
                """;

            command.ExecuteNonQuery();
        }
    }

    public static void InsertToWithoutTZ(this Npgsql.NpgsqlConnection connection, DateTime value)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO ts_wotz_test VALUES ($1)";
            command.Parameters.AddWithValue(value).DbType = System.Data.DbType.DateTime;
            command.ExecuteNonQuery();
        }
    }

    public static void InsertToWithTZ(this Npgsql.NpgsqlConnection connection, DateTime value)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO ts_wtz_test VALUES ($1)";
            command.Parameters.AddWithValue(value).DbType = System.Data.DbType.DateTime;
            command.ExecuteNonQuery();
        }
    }

    public static void InsertToDate(this Npgsql.NpgsqlConnection connection, DateTime value)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO date_test VALUES ($1)";
            command.Parameters.AddWithValue(value).DbType = System.Data.DbType.DateTime;
            command.ExecuteNonQuery();
        }
    }

    public static DateTime? SelectWithoutTZ(this Npgsql.NpgsqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM ts_wotz_test";
            return (DateTime?)command.ExecuteScalar();
        }
    }

    public static DateTime? SelectWithTZ(this Npgsql.NpgsqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM ts_wtz_test";
            return (DateTime?)command.ExecuteScalar();
        }
    }

    public static DateTime? SelectDate(this Npgsql.NpgsqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM date_test";
            return (DateTime?)command.ExecuteScalar();
        }
    }

    public static void TruncateTestTables(this Npgsql.NpgsqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                TRUNCATE TABLE ts_wotz_test;
                TRUNCATE TABLE ts_wtz_test;
                TRUNCATE TABLE date_test;
                """;
            command.ExecuteNonQuery();
        }
    }
}