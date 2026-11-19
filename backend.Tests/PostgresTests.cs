using GeneralExpenses.Api.Auth;
using GeneralExpenses.Api;
using GeneralExpenses.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

    [PostgresFact]
    public async Task SeparateReceiptsKeepItemsSelectionsAndUndoIsolated()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = await database.Run(s => s.CreateAsync(new() { RequestId = Guid.NewGuid(), Title = "Обед", Participants = ["Лена", "Миша"] }));
        var second = await database.Run(s => s.CreateAsync(new() { RequestId = Guid.NewGuid(), Title = "Кофе", Participants = ["Ира"] }));
        Assert.Empty(first.Items);
        Assert.Equal(new[] { "Лена", "Миша" }, first.Participants.Select(p => p.Name));
        first = await database.Run(s => s.AddAsync(Item(first, "Суп", 10001), receiptId: first.Id));
        var itemId = first.Items.Single().Id;
        first = await database.Run(s => s.SelectAsync(itemId, first.Participants[0].Id, Select(first), receiptId: first.Id));
        first = await database.Run(s => s.SelectAsync(itemId, first.Participants[1].Id, Select(first), receiptId: first.Id));
        first = await database.Run(s => s.ReadAsync(receiptId: first.Id));
        Assert.Equal(5001, first.Result.Totals[first.Participants[0].Id]);
        var wrongItem = await Assert.ThrowsAsync<ReceiptException>(() => database.Run(s => s.EditAsync(itemId, Item(second, "Чужое", 100), receiptId: second.Id)));
        Assert.Equal(404, wrongItem.Status);
        var wrongPerson = await Assert.ThrowsAsync<ReceiptException>(() => database.Run(s => s.SelectAsync(itemId, second.Participants[0].Id, Select(first), receiptId: first.Id)));
        Assert.Equal(400, wrongPerson.Status);
        first = await database.Run(s => s.DeleteAsync(itemId, Version(first), receiptId: first.Id));
        second = await database.Run(s => s.ResetAsync(Version(second), receiptId: second.Id));
        first = await database.Run(s => s.ReadAsync(receiptId: first.Id));
        Assert.NotNull(first.Deleted);
        first = await database.Run(s => s.UndoAsync(Version(first), receiptId: first.Id));
        Assert.Equal(10001, first.Result.Total);
        Assert.Equal(5001, first.Result.Totals[first.Participants[0].Id]);
        Assert.Empty((await database.Run(s => s.ReadAsync(receiptId: second.Id))).Items);
        await using var db = database.Context();
        var cards = await new ReceiptStore(db, new TestUser(-1)).ListAsync();
        Assert.Equal(3, cards.Length); // Два новых и исходный демочек.
        Assert.Equal(10001, cards.Single(x => x.Id == first.Id).Total);
        Assert.Equal(0, cards.Single(x => x.Id == second.Id).Total);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReceiptException>(() => database.Run(s => s.ReadAsync(receiptId: "missing")))).Status);
    }

    [PostgresFact]
    public async Task CreatingReceiptValidatesNamesAndIsIdempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var request = new CreateReceiptRequest { RequestId = Guid.NewGuid(), Title = "Ужин", Participants = ["Оля", "Пётр"] };
        var copies = await Task.WhenAll(database.Run(s => s.CreateAsync(request)), database.Run(s => s.CreateAsync(request)));
        Assert.Equal(copies[0].Id, copies[1].Id);
        var invalid = new[] {
            new CreateReceiptRequest { RequestId = Guid.NewGuid(), Title = " ", Participants = ["Аня"] },
            new CreateReceiptRequest { RequestId = Guid.NewGuid(), Title = "Ужин", Participants = [] },
            new CreateReceiptRequest { RequestId = Guid.NewGuid(), Title = "Ужин", Participants = ["Аня", " аня "] },
            new CreateReceiptRequest { RequestId = Guid.NewGuid(), Title = "Ужин", Participants = [new string('x', 101)] }
        };
        foreach (var input in invalid) Assert.Equal(400, (await Assert.ThrowsAsync<ReceiptException>(() => database.Run(s => s.CreateAsync(input)))).Status);
        await using var db = database.Context();
        Assert.Equal(2, (await new ReceiptStore(db, new TestUser(-1)).ListAsync()).Length);
    }

    [PostgresFact]
    public async Task MetadataMigrationPreservesExistingReceipt()
    {
        await using var database = await TestDatabase.CreateAsync(legacy: true);
        var state = await database.Run(s => s.ReadAsync());
        Assert.Equal("Ужин с друзьями", state.Title);
        Assert.Equal(7, state.Revision);
        Assert.Equal("old-instance", state.InstanceId);
        Assert.Equal(90000, state.Result.Totals["anya"]);
        Assert.Equal("Пицца", state.Items.Single().Name);
    }
    [PostgresFact]
    public async Task UsersCannotReadChangeOrRetryCreationOfAnotherUsersReceipt()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var db = database.Context())
        {
            db.Users.AddRange(new UserEntity { Id = 101, Name = "Первый" }, new UserEntity { Id = 202, Name = "Второй" });
            await db.SaveChangesAsync();
        }
        var request = new CreateReceiptRequest { RequestId = Guid.NewGuid(), Title = "Личный ужин", Participants = ["Аня"] };
        var owned = await database.Run(s => s.CreateAsync(request), 101);
        owned = await database.Run(s => s.AddAsync(Item(owned, "Суп", 10001), receiptId: owned.Id), 101);
        var item = owned.Items.Single().Id;
        Func<ReceiptStore, Task<ReceiptDto>>[] forbidden =
        [
            s => s.ReadAsync(receiptId: owned.Id),
            s => s.AddAsync(Item(owned, "Чужая запись", 1), receiptId: owned.Id),
            s => s.EditAsync(item, Item(owned, "Подмена", 1), receiptId: owned.Id),
            s => s.DeleteAsync(item, Version(owned), receiptId: owned.Id),
            s => s.SelectAsync(item, owned.Participants[0].Id, Select(owned), receiptId: owned.Id),
            s => s.ResetAsync(Version(owned), receiptId: owned.Id),
            s => s.UndoAsync(Version(owned), receiptId: owned.Id),
            s => s.CreateAsync(request)
        ];
        foreach (var action in forbidden)
            Assert.Equal(404, (await Assert.ThrowsAsync<ReceiptException>(() => database.Run(action, 202))).Status);
        await using (var db = database.Context())
        {
            Assert.Empty(await new ReceiptStore(db, new TestUser(202)).ListAsync());
            Assert.Equal(owned.Id, Assert.Single(await new ReceiptStore(db, new TestUser(101)).ListAsync()).Id);
            Assert.Equal("demo", Assert.Single(await new ReceiptStore(db, new TestUser(-1)).ListAsync()).Id);
        }
        var after = await database.Run(s => s.ReadAsync(receiptId: owned.Id), 101);
        Assert.Equal(owned.Revision, after.Revision);
        Assert.Equal(10001, after.Result.Total);
    }
    private sealed record TestUser(long Id) : ICurrentUser;
    private sealed class TestDatabase(string connection, string admin, string name) : IAsyncDisposable
    {
        public ExpensesDbContext Context() => new(new DbContextOptionsBuilder<ExpensesDbContext>().UseNpgsql(connection).Options);
        public async Task<ReceiptDto> Run(Func<ReceiptStore, Task<ReceiptDto>> action, long userId = -1)
        {
            await using var db = Context();
            return await action(new ReceiptStore(db, new TestUser(userId)));
        }
        public static async Task<TestDatabase> CreateAsync(bool legacy = false)
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
                if (legacy)
                {
                    var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
                    await migrator.MigrateAsync(db.Database.GetMigrations().First());
                    await db.Database.ExecuteSqlRawAsync("""
                        INSERT INTO receipts ("Id", "InstanceId", "Revision") VALUES ('demo', 'old-instance', 7);
                        INSERT INTO participants ("ReceiptId", "Id", "Name", "Initial", "Position") VALUES ('demo', 'anya', 'Аня', 'А', 0);
                        INSERT INTO items ("ReceiptId", "Id", "Name", "Detail", "Amount", "Position", "IsDeleted") VALUES ('demo', 'pizza', 'Пицца', '', 90000, 0, false);
                        INSERT INTO assignments ("ReceiptId", "ItemId", "ParticipantId") VALUES ('demo', 'pizza', 'anya');
                        """);
                }
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
