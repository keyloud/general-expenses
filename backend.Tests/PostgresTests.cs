using GeneralExpenses.Api;
using GeneralExpenses.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

// Эти тесты работают с настоящим PostgreSQL, в отдельных временных базах.
// Без явной строки подключения помечаются пропущенными, а не используют рабочую БД.
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EXPENSES_TEST_CONNECTION")))
            Skip = "Запустите scripts/Test-Backend.ps1 для интеграционных проверок PostgreSQL.";
    }
}

public sealed class PostgresTests
{
    private static ChangeRequest Version(ReceiptDto s) => new() { InstanceId = s.InstanceId, Revision = s.Revision };
    private static ItemRequest Item(ReceiptDto s, string name, long amount) => new() { InstanceId = s.InstanceId, Revision = s.Revision, Name = name, Amount = amount };
    private static SelectionRequest Select(ReceiptDto s) => new() { InstanceId = s.InstanceId, Revision = s.Revision, Selected = true };

    [PostgresFact]
    public async Task PersistenceUndoResetAndEmptyReceiptSurviveNewContexts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var state = await database.Run(s => s.ReadAsync());
        var initialId = state.InstanceId;
        state = await database.Run(s => s.SelectAsync("pizza", "anya", Select(state)));
        state = await database.Run(s => s.SelectAsync("pizza", "boris", Select(state)));
        state = await database.Run(s => s.EditAsync("pizza", Item(state, "Большая пицца", 100001)));
        state = await database.Run(s => s.ReadAsync());
        Assert.Equal(50001, state.Result.Totals["anya"]);
        Assert.Equal(50000, state.Result.Totals["boris"]);
        state = await database.Run(s => s.DeleteAsync("pizza", Version(state)));
        state = await database.Run(s => s.ReadAsync());
        Assert.Equal("pizza", state.Deleted!.Item.Id);
        state = await database.Run(s => s.UndoAsync(Version(state)));
        Assert.Equal("pizza", state.Items[0].Id);
        Assert.Equal(50001, state.Result.Totals["anya"]);
        state = await database.Run(s => s.DeleteAsync("pizza", Version(state)));
        // Второе удаление должно заменить предыдущую возможность отмены.
        state = await database.Run(s => s.DeleteAsync("pasta", Version(state)));
        state = await database.Run(s => s.UndoAsync(Version(state)));
        Assert.DoesNotContain(state.Items, i => i.Id == "pizza");
        Assert.Contains(state.Items, i => i.Id == "pasta");
        state = await database.Run(s => s.SelectAsync("pasta", "anya", Select(state)));
        state = await database.Run(s => s.DeleteAsync("pasta", Version(state)));
        state = await database.Run(s => s.ResetAsync(Version(state)));
        state = await database.Run(s => s.UndoAsync(Version(state)));
        Assert.Empty(state.Assignments["pasta"]);
        foreach (var item in state.Items) state = await database.Run(s => s.DeleteAsync(item.Id, Version(state)));
        state = await database.Run(s => s.ReadAsync());
        Assert.Empty(state.Items);
        Assert.Equal(0, state.Result.Total);
        // Повторный seed не восстанавливает намеренно пустой чек.
        await using (var db = database.Context()) await ReceiptStore.InitializeAsync(db);
        state = await database.Run(s => s.ReadAsync());
        Assert.Empty(state.Items);
        Assert.Equal(initialId, state.InstanceId);
        state = await database.Run(s => s.AddAsync(Item(state, "Новая позиция", 1)));
        Assert.Equal(1, state.Result.Total);
    }

    [PostgresFact]
    public async Task ConcurrentWritesAndInvalidChangesAreAtomic()
    {
        await using var database = await TestDatabase.CreateAsync();
        var state = await database.Run(s => s.ReadAsync());
        async Task<int> Add()
        {
            try { await database.Run(s => s.AddAsync(Item(state, "Конкурирующая запись", 100))); return 200; }
            catch (ReceiptException e) { return e.Status; }
        }
        var statuses = await Task.WhenAll(Add(), Add());
        Assert.Equal(new[] { 200, 409 }, statuses.Order().ToArray());
        var after = await database.Run(s => s.ReadAsync());
        Assert.Equal(state.Items.Length + 1, after.Items.Length);
        Assert.Equal(state.Revision + 1, after.Revision);
        await Assert.ThrowsAsync<ReceiptException>(() => database.Run(s => s.AddAsync(Item(after, "Ошибка", -1))));
        Assert.Equal(after.Revision, (await database.Run(s => s.ReadAsync())).Revision);
        // Проверяем реальные внешние ключи, а не только правила C#.
        await using var db = database.Context();
        db.Set<AssignmentEntity>().Add(new() { ItemId = "pizza", ParticipantId = "missing" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private sealed class TestDatabase(string connection, string admin, string name) : IAsyncDisposable
    {
        public ExpensesDbContext Context() => new(new DbContextOptionsBuilder<ExpensesDbContext>().UseNpgsql(connection).Options);
        public async Task<ReceiptDto> Run(Func<ReceiptStore, Task<ReceiptDto>> action)
        {
            await using var db = Context();
            return await action(new ReceiptStore(db));
        }
        public static async Task<TestDatabase> CreateAsync()
        {
            var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("EXPENSES_TEST_CONNECTION")) { Database = "postgres" };
            var admin = builder.ConnectionString;
            var name = "expenses_test_" + Guid.NewGuid().ToString("N");
            await using var sql = new NpgsqlConnection(admin);
            await sql.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE {name}", sql).ExecuteNonQueryAsync();
            builder.Database = name;
            var result = new TestDatabase(builder.ConnectionString, admin, name);
            try
            {
                await using var db = result.Context();
                await db.Database.MigrateAsync();
                await db.Database.MigrateAsync(); // Повторное применение не меняет схему.
                await ReceiptStore.InitializeAsync(db);
                return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            // Имя генерируется здесь, не берётся из внешней конфигурации или рабочего чека.
            await using var sql = new NpgsqlConnection(admin);
            await sql.OpenAsync();
            await new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", sql).ExecuteNonQueryAsync();
        }
    }
}
