using DokployMonitor.Core.Deployments;
using DokployMonitor.Core.Notifications;
using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Infrastructure.Persistence.Migrations;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DokployMonitor.Tests;

/// <summary>
/// Sema FluentMigrator ile el yazimi, sorgular ise EF Core ile yapiliyor. Bu iki tarafin
/// birbirinden sapmasi (kolon adi, nullability, cascade) ancak calisma zamaninda patlar —
/// bu testler sapmayi derleme/CI asamasinda yakalar.
/// </summary>
public sealed class MigrationSchemaTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dokploy-monitor-tests").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Test temizligi; dosya kilitliyse onemsiz.
        }
    }

    [Fact]
    public void MigrateUp_bos_veritabaninda_semayi_olusturur()
    {
        var connectionString = ConnectionString("fresh.db");

        RunMigrations(connectionString, out var runner);

        Assert.False(runner.HasMigrationsToApplyUp());
        Assert.Equal(
            [
                "AspNetRoleClaims", "AspNetRoles", "AspNetUserClaims", "AspNetUserLogins",
                "AspNetUserRoles", "AspNetUserTokens", "AspNetUsers",
                "DeploymentEvents", "Deployments", "DokployConnections",
                "ErrorSignatures", "WebhookNotifications",
            ],
            TableNames(connectionString));
    }

    [Fact]
    public void FluentMigrator_semasi_EF_modeliyle_ayni_kolonlari_uretir()
    {
        var fluent = ConnectionString("fluent.db");
        var ef = ConnectionString("ef.db");

        RunMigrations(fluent, out _);

        using (var context = CreateContext(ef))
        {
            // EF'in kendi modelinden urettigi sema: karsilastirma referansimiz.
            context.Database.EnsureCreated();
        }

        var fromFluentMigrator = ReadColumns(fluent);
        var fromEfCore = ReadColumns(ef);

        Assert.Equal(fromEfCore.Keys.Order(), fromFluentMigrator.Keys.Order());

        foreach (var (table, expected) in fromEfCore)
        {
            Assert.Equal(expected, fromFluentMigrator[table]);
        }
    }

    [Fact]
    public async Task EF_Core_FluentMigrator_semasina_yazip_okuyabilir()
    {
        var connectionString = ConnectionString("crud.db");
        RunMigrations(connectionString, out _);

        var createdAt = new DateTimeOffset(2026, 7, 26, 9, 30, 15, TimeSpan.Zero);

        await using (var context = CreateContext(connectionString))
        {
            context.Deployments.Add(new TrackedDeployment
            {
                DeploymentId = "dep-1",
                ServiceType = "application",
                Status = DeploymentStatus.Error,
                CreatedAt = createdAt,
                FirstSeenAt = createdAt,
                LastUpdatedAt = createdAt,
                ErrorSignatureHash = "abc123",
                IsPreviewDeployment = true,
                DurationSeconds = 42,
            });

            context.DeploymentEvents.Add(new DeploymentEvent
            {
                DeploymentId = "dep-1",
                EventType = DeploymentEventType.Finished,
                Source = DeploymentEventSource.Poll,
                FromStatus = DeploymentStatus.Running,
                ToStatus = DeploymentStatus.Error,
                OccurredAt = createdAt,
            });

            context.ErrorSignatures.Add(new ErrorSignature
            {
                Hash = "abc123",
                NormalizedMessage = "npm ERR!",
                SampleMessage = "npm ERR! code ELIFECYCLE",
                OccurrenceCount = 1,
                FirstSeenAt = createdAt,
                LastSeenAt = createdAt,
            });

            context.WebhookNotifications.Add(new WebhookNotification
            {
                ReceivedAt = createdAt,
                Status = "error",
                Type = "build",
            });

            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(connectionString))
        {
            var deployment = await context.Deployments.SingleAsync();
            Assert.Equal(DeploymentStatus.Error, deployment.Status);
            Assert.Equal(createdAt, deployment.CreatedAt);
            Assert.True(deployment.IsPreviewDeployment);
            Assert.Equal(42, deployment.DurationSeconds);

            // Identity kolonlari SQLite tarafindan atanmali.
            Assert.True(await context.WebhookNotifications.AnyAsync(n => n.Id > 0));
            Assert.True(await context.DeploymentEvents.AnyAsync(e => e.Id > 0));
        }
    }

    [Fact]
    public async Task Deployment_silinince_olay_kayitlari_cascade_ile_silinir()
    {
        // RetentionWorker ExecuteDelete kullaniyor: EF change tracker devrede olmadigi icin
        // olaylari temizleyen sey veritabanindaki ON DELETE CASCADE.
        var connectionString = ConnectionString("cascade.db");
        RunMigrations(connectionString, out _);

        var now = DateTimeOffset.UtcNow;

        await using (var context = CreateContext(connectionString))
        {
            context.Deployments.Add(new TrackedDeployment
            {
                DeploymentId = "dep-2",
                ServiceType = "compose",
                CreatedAt = now,
                FirstSeenAt = now,
                LastUpdatedAt = now,
            });

            context.DeploymentEvents.Add(new DeploymentEvent
            {
                DeploymentId = "dep-2",
                EventType = DeploymentEventType.Started,
                Source = DeploymentEventSource.Queue,
                OccurredAt = now,
            });

            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(connectionString))
        {
            await context.Deployments.Where(d => d.DeploymentId == "dep-2").ExecuteDeleteAsync();

            Assert.Empty(await context.DeploymentEvents.ToListAsync());
        }
    }

    [Fact]
    public void MigrateUp_EF_migrationlariyla_olusmus_veritabanini_devralir()
    {
        // Onceki surumler semayi EF Core migration'lari ile olusturuyordu. Bu veritabanlarinda
        // tablolar zaten var; migration onlari yeniden yaratmayi denememeli.
        var connectionString = ConnectionString("legacy.db");

        using (var context = CreateContext(connectionString))
        {
            context.Database.EnsureCreated();
        }

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL, ProductVersion TEXT NOT NULL);"
                + "INSERT INTO __EFMigrationsHistory VALUES ('20260725190756_InitialCreate', '10.0.10');"
                + "CREATE TABLE __EFMigrationsLock (Id INTEGER NOT NULL, Timestamp TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }

        RunMigrations(connectionString, out var runner);

        var tables = TableNames(connectionString);
        Assert.False(runner.HasMigrationsToApplyUp());
        Assert.DoesNotContain("__EFMigrationsHistory", tables);
        Assert.DoesNotContain("__EFMigrationsLock", tables);
        Assert.Contains("Deployments", tables);
    }

    private string ConnectionString(string fileName) =>
        $"Data Source={Path.Combine(_directory, fileName)}";

    private static MonitorDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<MonitorDbContext>().UseSqlite(connectionString).Options);

    private static void RunMigrations(string connectionString, out IMigrationRunner runner)
    {
        var provider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(builder => builder
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InitialSchema).Assembly).For.Migrations())
            .BuildServiceProvider(validateScopes: false);

        runner = provider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
    }

    private static List<string> TableNames(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' "
            + "AND name <> 'VersionInfo' ORDER BY name;";

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>Tablo -> "kolon:tip:notnull" listesi. Kolon sirasi degil, icerik karsilastirilir.</summary>
    private static Dictionary<string, List<string>> ReadColumns(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var schema = new Dictionary<string, List<string>>();

        foreach (var table in TableNames(connectionString))
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{table}');";

            var columns = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(reader.GetOrdinal("name"));
                var type = reader.GetString(reader.GetOrdinal("type"));
                var notNull = reader.GetInt32(reader.GetOrdinal("notnull"));
                var primaryKey = reader.GetInt32(reader.GetOrdinal("pk"));
                columns.Add($"{name}:{type.ToUpperInvariant()}:notnull={notNull}:pk={primaryKey}");
            }

            schema[table] = [.. columns.Order()];
        }

        return schema;
    }
}
