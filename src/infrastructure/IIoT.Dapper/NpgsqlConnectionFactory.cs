using System.Data;
using Npgsql;

namespace IIoT.Dapper;

internal class NpgsqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(connectionString);
    }
}
