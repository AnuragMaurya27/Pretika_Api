using Npgsql;
using System.Data;

namespace HauntedVoiceUniverse.Infrastructure.Database;

/// <summary>
/// ADO.NET PostgreSQL Connection Factory
/// Har jagah se NpgsqlConnection isse lo - pool managed rahega
/// </summary>
public interface IDbConnectionFactory
{
    Task<NpgsqlConnection> CreateConnectionAsync();
    NpgsqlConnection CreateConnection();
}

public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection string not found.");
    }

    public async Task<NpgsqlConnection> CreateConnectionAsync()
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

    public NpgsqlConnection CreateConnection()
    {
        var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}