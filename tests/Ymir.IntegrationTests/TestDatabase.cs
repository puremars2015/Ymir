using Microsoft.Data.SqlClient;

namespace Ymir.IntegrationTests;

/// <summary>
/// 每個 test fixture 一個獨立的 SQL Server 資料庫，結束後刪除。
/// 伺服器位址取自 <c>YMIR_TEST_SQLSERVER</c>（不含 Database），預設為本機開發用 SQL Server（見 CLAUDE.md）。
/// </summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    public const string DefaultServer = "Server=localhost,1433;User Id=sa;Password=Ymir_Dev_Passw0rd!;TrustServerCertificate=True";

    private readonly string _serverConnectionString;

    public TestDatabase()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable("YMIR_TEST_SQLSERVER") ?? DefaultServer;
        Name = $"ymir_test_{Guid.NewGuid():N}";
        ConnectionString = new SqlConnectionStringBuilder(_serverConnectionString) { InitialCatalog = Name }.ConnectionString;
    }

    public string Name { get; }

    public string ConnectionString { get; }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        var connection = new SqlConnection(_serverConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
#pragma warning disable CA2100 // 資料庫名稱由本類別以 GUID 產生，不含外部輸入。
                command.CommandText = $"IF DB_ID('{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END";
#pragma warning restore CA2100
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }
}
