using Npgsql;
using System.Data;

namespace HauntedVoiceUniverse.Infrastructure.Database;

/// <summary>
/// ADO.NET helper - common reusable methods
/// Baar baar try/catch likhne ki zarurat nahi
/// </summary>
public static class DbHelper
{
    // ─── Execute Non-Query (INSERT / UPDATE / DELETE) ─────────────────────────

    public static async Task<int> ExecuteNonQueryAsync(
        NpgsqlConnection conn,
        string sql,
        Dictionary<string, object?>? parameters = null,
        NpgsqlTransaction? transaction = null)
    {
        using var cmd = new NpgsqlCommand(sql, conn, transaction);
        AddParameters(cmd, parameters);
        return await cmd.ExecuteNonQueryAsync();
    }

    // ─── Execute Scalar (Single value return) ─────────────────────────────────

    public static async Task<T?> ExecuteScalarAsync<T>(
        NpgsqlConnection conn,
        string sql,
        Dictionary<string, object?>? parameters = null,
        NpgsqlTransaction? transaction = null)
    {
        using var cmd = new NpgsqlCommand(sql, conn, transaction);
        AddParameters(cmd, parameters);
        var result = await cmd.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value) return default;
        // Guid doesn't implement IConvertible — handle directly
        if (result is T directMatch) return directMatch;
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType == typeof(Guid) && result is Guid g) return (T)(object)g;
        if (targetType == typeof(Guid) && result is string s && Guid.TryParse(s, out var pg)) return (T)(object)pg;
        return (T)Convert.ChangeType(result, targetType);
    }

    // ─── Execute Reader (Multiple rows) ───────────────────────────────────────

    public static async Task<List<Dictionary<string, object?>>> ExecuteReaderAsync(
        NpgsqlConnection conn,
        string sql,
        Dictionary<string, object?>? parameters = null)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        AddParameters(cmd, parameters);

        var rows = new List<Dictionary<string, object?>>();
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }

        return rows;
    }

    // ─── Execute Reader with Mapper ───────────────────────────────────────────

    public static async Task<List<T>> ExecuteReaderAsync<T>(
        NpgsqlConnection conn,
        string sql,
        Func<NpgsqlDataReader, T> mapper,
        Dictionary<string, object?>? parameters = null)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        AddParameters(cmd, parameters);

        var results = new List<T>();
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(mapper(reader));
        }

        return results;
    }

    // ─── Execute Reader Single Row ─────────────────────────────────────────────

    public static async Task<T?> ExecuteReaderFirstAsync<T>(
        NpgsqlConnection conn,
        string sql,
        Func<NpgsqlDataReader, T> mapper,
        Dictionary<string, object?>? parameters = null)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        AddParameters(cmd, parameters);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return mapper(reader);

        return default;
    }

    // ─── Private: Add Parameters ──────────────────────────────────────────────

    private static void AddParameters(NpgsqlCommand cmd, Dictionary<string, object?>? parameters)
    {
        if (parameters == null) return;
        foreach (var param in parameters)
        {
            cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
        }
    }

    // ─── Safe Reader Helpers ──────────────────────────────────────────────────

    public static Guid GetGuid(NpgsqlDataReader r, string col) =>
        r.GetGuid(r.GetOrdinal(col));

    public static string GetString(NpgsqlDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? "" : r.GetString(r.GetOrdinal(col));

    public static string? GetStringOrNull(NpgsqlDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetString(r.GetOrdinal(col));

    public static int GetInt(NpgsqlDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? 0 : r.GetInt32(r.GetOrdinal(col));

    public static long GetLong(NpgsqlDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? 0L : r.GetInt64(r.GetOrdinal(col));

    public static bool GetBool(NpgsqlDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? false : r.GetBoolean(r.GetOrdinal(col));

    public static decimal GetDecimal(NpgsqlDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? 0m : r.GetDecimal(r.GetOrdinal(col));

    public static DateTime? GetDateTimeOrNull(NpgsqlDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetDateTime(r.GetOrdinal(col));

    public static DateTime GetDateTime(NpgsqlDataReader r, string col) =>
        r.GetDateTime(r.GetOrdinal(col));

    public static T GetEnum<T>(NpgsqlDataReader r, string col) where T : struct, Enum =>
        Enum.Parse<T>(GetString(r, col), true);
}