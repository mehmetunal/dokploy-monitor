using DokployMonitor.Core.Deployments;
using DokployMonitor.Core.Notifications;
using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Infrastructure.Persistence.Migrations;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace DokployMonitor.Tests;

/// <summary>
/// Sema FluentMigrator ile el yazimi, sorgular ise EF Core ile yapiliyor. Bu iki tarafin
/// birbirinden sapmasi (kolon adi, nullability, cascade) ancak calisma zamaninda patlar —
/// bu testler sapmayi derleme/CI asamasinda yakalar.
/// </summary>
[Collection("SqlServer")]
public sealed class MigrationSchemaTests
{
    private readonly SqlServerFixture _fixture;

    public MigrationSchemaTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public void MigrateUp_bos_veritabaninda_semayi_olusturur()
    {
        var connectionString = _fixture.CreateDatabase();

        RunMigrations(connectionString, out var runner);

        Assert.False(runner.HasMigrationsToApplyUp());
        Assert.Equal(
            [
                "AspNetRoleClaims", "AspNetRoles", "AspNetUserClaims", "AspNetUserLogins",
                "AspNetUserRoles", "AspNetUsers", "AspNetUserTokens",
                "DeploymentEvents", "Deployments", "DokployConnections",
                "ErrorSignatures", "Translations", "WebhookNotifications",
            ],
            TableNames(connectionString));
    }

    [Fact]
    public void FluentMigrator_semasi_EF_modeliyle_ayni_kolonlari_uretir()
    {
        var fluent = _fixture.CreateDatabase();
        var ef = _fixture.CreateDatabase();

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
        var connectionString = _fixture.CreateDatabase();
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

            Assert.True(await context.WebhookNotifications.AnyAsync(n => n.Id > 0));
            Assert.True(await context.DeploymentEvents.AnyAsync(e => e.Id > 0));
        }
    }

    [Fact]
    public async Task Deployment_silinince_olay_kayitlari_cascade_ile_silinir()
    {
        // RetentionWorker ExecuteDelete kullaniyor: EF change tracker devrede olmadigi icin
        // olaylari temizleyen sey veritabanindaki ON DELETE CASCADE.
        var connectionString = _fixture.CreateDatabase();
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
        var connectionString = _fixture.CreateDatabase();

        using (var context = CreateContext(connectionString))
        {
            context.Database.EnsureCreated();
        }

        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE __EFMigrationsHistory (
                    MigrationId nvarchar(150) NOT NULL,
                    ProductVersion nvarchar(32) NOT NULL);
                INSERT INTO __EFMigrationsHistory VALUES (N'20260725190756_InitialCreate', N'10.0.10');
                CREATE TABLE __EFMigrationsLock (Id int NOT NULL, Timestamp datetimeoffset NOT NULL);
                """;
            command.ExecuteNonQuery();
        }

        RunMigrations(connectionString, out var runner);

        var tables = TableNames(connectionString);
        Assert.False(runner.HasMigrationsToApplyUp());
        Assert.DoesNotContain("__EFMigrationsHistory", tables);
        Assert.DoesNotContain("__EFMigrationsLock", tables);
        Assert.Contains("Deployments", tables);
    }

    private static MonitorDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<MonitorDbContext>().UseSqlServer(connectionString).Options);

    private static void RunMigrations(string connectionString, out IMigrationRunner runner)
    {
        var provider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(builder => builder
                .AddSqlServer()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InitialSchema).Assembly).For.Migrations())
            .BuildServiceProvider(validateScopes: false);

        runner = provider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
    }

    private static List<string> TableNames(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
              AND TABLE_NAME <> 'VersionInfo'
            ORDER BY TABLE_NAME;
            """;

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
        using var connection = new SqlConnection(connectionString);
        connection.Open();

        var schema = new Dictionary<string, List<string>>();

        foreach (var table in TableNames(connectionString))
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    c.COLUMN_NAME,
                    c.DATA_TYPE,
                    c.CHARACTER_MAXIMUM_LENGTH,
                    c.IS_NULLABLE,
                    COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_NAME = @table
                ORDER BY c.COLUMN_NAME;
                """;
            command.Parameters.AddWithValue("@table", table);

            var columns = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var dataType = reader.GetString(1);
                var maxLen = reader.IsDBNull(2) ? null : reader.GetInt32(2).ToString();
                var nullable = reader.GetString(3);
                var identity = reader.GetInt32(4);
                var type = maxLen is null ? dataType : $"{dataType}({maxLen})";
                columns.Add($"{name}:{type}:nullable={nullable}:identity={identity}");
            }

            schema[table] = columns;
        }

        return schema;
    }
}

[CollectionDefinition("SqlServer")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public string CreateDatabase()
    {
        var name = "dm_" + Guid.NewGuid().ToString("N");
        var master = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "master",
        };

        using (var connection = new SqlConnection(master.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{name}]";
            command.ExecuteNonQuery();
        }

        var target = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = name,
        };

        return target.ConnectionString;
    }
}
