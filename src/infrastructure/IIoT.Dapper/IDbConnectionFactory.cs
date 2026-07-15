using System.Data;

namespace IIoT.Dapper;

/// <summary>
/// 数据库连接工厂契约
/// Dapper 查询层通过此工厂获取原生 ADO.NET 连接，与 EF Core 完全隔离
/// </summary>
internal interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
