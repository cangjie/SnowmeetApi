using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Tests;

public class MiniTicketMarkerPrecisionTests
{
    [Fact]
    public void SqlServerMarkerTimeParameterUsesDatetimeColumnType()
    {
        using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>()
            .UseSqlServer("Server=localhost;Database=unused;Integrated Security=true;TrustServerCertificate=true")
            .Options);
        var mapping = db.Model.FindEntityType(typeof(TicketLog))!
            .FindProperty(nameof(TicketLog.transact_time))!.GetRelationalTypeMapping();
        using var command = db.Database.GetDbConnection().CreateCommand();
        var parameter = mapping.CreateParameter(command, "time", new DateTime(2026, 11, 7, 8, 32, 45, 827));

        Assert.Equal("datetime", mapping.StoreType);
        Assert.Equal(DbType.DateTime, parameter.DbType);

        var ticket = new Ticket { code = "test", shared = 1, shared_time = new DateTime(2026, 11, 7, 8, 32, 45, 827) };
        var sql = db.ticketLog.Where(l => l.code == ticket.code && l.memo == MiniTicketRules.ShareMarker
            && l.accepter_open_id == "" && l.transact_time == ticket.shared_time
            && !db.ticketLog.Any(replaced => replaced.code == l.code && replaced.memo == MiniTicketRules.ShareReplacedMarker
                && replaced.accepter_open_id == "" && replaced.transact_time == l.transact_time)).ToQueryString();
        Assert.Matches(@"DECLARE @__ticket_shared_time_\d+ datetime\s*=", sql);
    }

    [TicketSqlServerFact]
    public async Task SqlServerDatetimeComparisonCoversEveryMillisecondAndAdjacentTicks()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNOWMEET_TICKET_READONLY_SQLSERVER"))
        {
            ApplicationIntent = ApplicationIntent.ReadOnly
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH digits(n) AS (SELECT n FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(n)),
            times AS (
                SELECT CAST(DATEADD(millisecond, a.n + 10*b.n + 100*c.n,
                    CAST('2026-11-07T08:32:45' AS datetime)) AS datetime) AS t
                FROM digits a CROSS JOIN digits b CROSS JOIN digits c
            )
            SELECT COUNT(*),
                SUM(CASE WHEN t = CAST(CAST(t AS datetime2(3)) AS datetime2(7)) THEN 0 ELSE 1 END),
                SUM(CASE WHEN t = CAST(CAST(t AS datetime2(3)) AS datetime) THEN 0 ELSE 1 END),
                SUM(CASE WHEN t = DATEADD(millisecond, 10, t) THEN 1 ELSE 0 END)
            FROM times
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1000, reader.GetInt32(0));
        Assert.True(reader.GetInt32(1) > 0);
        Assert.Equal(0, reader.GetInt32(2));
        Assert.Equal(0, reader.GetInt32(3));
    }

    [Fact]
    public async Task ReplacedMarkerOnlyInvalidatesMatchingShareAndPreservesAudit()
    {
        await using var f = await TicketMigrationTests.Fixture.Create();
        var ticket = f.Ticket();
        var firstTime = new DateTime(2026, 11, 7, 8, 32, 45, 827);
        var secondTime = firstTime.AddMilliseconds(10);
        ticket.shared_time = firstTime;
        f.Db.ticket.Add(ticket);
        f.Db.ticketLog.Add(new TicketLog { code = ticket.code, sender_open_id = "sender", accepter_open_id = "",
            memo = MiniTicketRules.ShareMarker, transact_time = firstTime });
        await f.Db.SaveChangesAsync();

        Assert.True(await MiniTicketRules.HasCurrentMarker(f.Db, ticket));
        f.Db.ticketLog.Add(new TicketLog { code = ticket.code, sender_open_id = "sender", accepter_open_id = "",
            memo = MiniTicketRules.ShareReplacedMarker, transact_time = firstTime });
        await f.Db.SaveChangesAsync();
        Assert.False(await MiniTicketRules.HasCurrentMarker(f.Db, ticket));

        ticket.shared_time = secondTime;
        f.Db.ticketLog.Add(new TicketLog { code = ticket.code, sender_open_id = "sender", accepter_open_id = "",
            memo = MiniTicketRules.ShareMarker, transact_time = secondTime });
        await f.Db.SaveChangesAsync();
        Assert.True(await MiniTicketRules.HasCurrentMarker(f.Db, ticket));
        Assert.Equal(3, await f.Db.ticketLog.CountAsync());

        ticket.shared = 0;
        Assert.False(await MiniTicketRules.HasCurrentMarker(f.Db, ticket));
        ticket.shared = 1;
        ticket.shared_time = null;
        Assert.False(await MiniTicketRules.HasCurrentMarker(f.Db, ticket));
    }

    public sealed class TicketSqlServerFactAttribute : FactAttribute
    {
        public TicketSqlServerFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SNOWMEET_TICKET_READONLY_SQLSERVER")))
                Skip = "需要只读 SQL Server 连接以验证 datetime 精度";
        }
    }
}
