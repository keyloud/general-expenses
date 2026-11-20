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

public sealed partial class PostgresTests
{
    private static ChangeRequest Version(ReceiptDto s) => new() { InstanceId = s.InstanceId, Revision = s.Revision };
    private static ItemRequest Item(ReceiptDto s, string name, long amount) => new() { InstanceId = s.InstanceId, Revision = s.Revision, Name = name, Amount = amount };
    private static SelectionRequest Select(ReceiptDto s) => new() { InstanceId = s.InstanceId, Revision = s.Revision, Selected = true, AdminMode = true };

    [PostgresFact]
    public async Task PersistenceDeleteResetAndEmptyReceiptSurviveNewContexts()
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
        await using (var check = database.Context())
        {
            Assert.False(await check.Set<ItemEntity>().AnyAsync(i => i.Id == "pizza"));
            Assert.False(await check.Set<AssignmentEntity>().AnyAsync(a => a.ItemId == "pizza"));
        }
        Assert.DoesNotContain(state.Items, i => i.Id == "pizza");
        Assert.Contains(state.Items, i => i.Id == "pasta");
        state = await database.Run(s => s.SelectAsync("pasta", "anya", Select(state)));
        state = await database.Run(s => s.DeleteAsync("pasta", Version(state)));
        state = await database.Run(s => s.ResetAsync(Version(state)));

        Assert.False(state.Assignments.ContainsKey("pasta"));
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
    public async Task SeparateReceiptsKeepItemsAndSelectionsIsolated()
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
        Assert.Empty(first.Items);
        Assert.Empty((await database.Run(s => s.ReadAsync(receiptId: second.Id))).Items);
        await using var db = database.Context();
        var cards = await new ReceiptStore(db, new TestUser(-1)).ListAsync();
        Assert.Equal(3, cards.Length); // Два новых и исходный демочек.
        Assert.Equal(0, cards.Single(x => x.Id == first.Id).Total);
        Assert.Equal(0, cards.Single(x => x.Id == second.Id).Total);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReceiptException>(() => database.Run(s => s.ReadAsync(receiptId: "missing")))).Status);
    }

    [PostgresFact]
    public async Task SwitchingSplitModeKeepsEveryoneWhoSelectedTheItem()
    {
        await using var database = await TestDatabase.CreateAsync();
        var state = await database.Run(s => s.ReadAsync());
        ItemRequest Beer(bool byPortion, int quantity = 5) => new()
        {
            Name = "Пиво", Amount = quantity * 30000, ByPortion = byPortion, Quantity = quantity,
            Revision = state.Revision, InstanceId = state.InstanceId
        };
        SelectionRequest Take(int count) => new()
        {
            Selected = true, Count = count, AdminMode = true, Revision = state.Revision, InstanceId = state.InstanceId
        };
        state = await database.Run(s => s.AddAsync(Beer(true)));
        var id = state.Items.Last().Id;
        state = await database.Run(s => s.SelectAsync(id, "boris", Take(1)));
        state = await database.Run(s => s.SelectAsync(id, "anya", Take(2)));
        state = await database.Run(s => s.EditAsync(id, Beer(false)));
        state = await database.Run(s => s.ReadAsync());
        Assert.Equal(new[] { "anya", "boris" }, state.Assignments[id]);
        Assert.Null(state.Items.Single(i => i.Id == id).Counts);
        Assert.Equal(75000, state.Result.Totals["anya"]);
        Assert.Equal(75000, state.Result.Totals["boris"]);
        Assert.Equal(0, state.Result.Totals["vika"]);
        Assert.Equal(400, (await Assert.ThrowsAsync<ReceiptException>(() =>
            database.Run(s => s.EditAsync(id, Beer(true, 1))))).Status);
        state = await database.Run(s => s.EditAsync(id, Beer(true)));
        state = await database.Run(s => s.ReadAsync());
        var counts = state.Items.Single(i => i.Id == id).Counts!;
        Assert.Equal(3, counts["anya"]);
        Assert.Equal(2, counts["boris"]);
        Assert.Equal(90000, state.Result.Totals["anya"]);
        Assert.Equal(60000, state.Result.Totals["boris"]);
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
    [PostgresFact]
    public async Task PersonalInviteCanBeClaimedOnceAndOnlyGrantsOwnSelection()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var db = database.Context())
        {
            db.Users.AddRange(new UserEntity { Id = 101, Name = "Аня" }, new UserEntity { Id = 202, Name = "Вика" });
            await db.SaveChangesAsync();
        }
        async Task<T> Invite<T>(long id, Func<InvitationStore, Task<T>> action)
        {
            await using var db = database.Context();
            return await action(new InvitationStore(db, new TestUser(id)));
        }
        const string receipt = "demo";
        var old = await Invite(-1, s => s.Issue(receipt, "anya", default));
        var fresh = await Invite(-1, s => s.Issue(receipt, "anya", default));
        Assert.Equal(404, (await Assert.ThrowsAsync<ReceiptException>(() => Invite(101, s => s.Preview(old.Token, default)))).Status);
        var info = await Invite(101, s => s.Preview(fresh.Token, default));
        Assert.Equal("Аня", info.ParticipantName);
        async Task<long> TryAccept(long id)
        {
            try { await Invite(id, s => s.Accept(fresh.Token, default)); return id; }
            catch (ReceiptException e) when (e.Status == 404) { return 0; }
        }
        var winners = await Task.WhenAll(TryAccept(101), TryAccept(202));
        var winner = Assert.Single(winners, id => id != 0);
        Assert.Equal(receipt, (await Invite(winner, s => s.Accept(fresh.Token, default))).ReceiptId);
        var another = await Invite(-1, s => s.Issue(receipt, "boris", default));
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() => Invite(winner, s => s.Accept(another.Token, default)))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReceiptException>(() => Invite(winner, s => s.Issue(receipt, "boris", default)))).Status);
        var ownerView = await database.Run(s => s.ReadAsync());
        Assert.Null(ownerView.MyParticipantId);
        Assert.True(ownerView.Participants.Single(p => p.Id == "anya").Joined);
        var selected = await database.Run(s => s.SelectAsync("pizza", "boris", Select(ownerView)));
        var guest = await database.Run(s => s.SelectAsync("pizza", "anya", Select(selected)), winner);
        Assert.Equal("anya", guest.MyParticipantId);
        Assert.Equal(45000, guest.Result.Totals["anya"]);
        Assert.Equal(45000, (await database.Run(s => s.ReadAsync())).Result.Totals["boris"]);
        foreach (Func<ReceiptStore, Task<ReceiptDto>> action in new Func<ReceiptStore, Task<ReceiptDto>>[] {
            s => s.SelectAsync("pizza", "boris", Select(guest)), s => s.DeleteAsync("pizza", Version(guest)),
            s => s.EditAsync("pizza", Item(guest, "Чужая правка", 500)), s => s.ResetAsync(Version(guest)), s => s.AddAsync(Item(guest, "Суп", 100)) })
            Assert.Equal(403, (await Assert.ThrowsAsync<ReceiptException>(() => database.Run(action, winner))).Status);
        await using var check = database.Context();
        Assert.Equal(receipt, Assert.Single(await new ReceiptStore(check, new TestUser(winner)).ListAsync()).Id);
        var boris = await check.Set<ParticipantEntity>().SingleAsync(p => p.Id == "boris");
        boris.InviteExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await check.SaveChangesAsync();
        Assert.Equal(404, (await Assert.ThrowsAsync<ReceiptException>(() => Invite(202, s => s.Accept(another.Token, default)))).Status);
    }

    [PostgresFact]
    public async Task OnlyOwnerCanDeleteCurrentVersionAndDeletionSurvivesRestart()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Context();
        var owner = new ReceiptStore(db, new TestUser(-1));
        var guest = new ReceiptStore(db, new TestUser(101));
        var invite = new InvitationStore(db, new TestUser(-1));
        db.Users.Add(new UserEntity { Id = 101, Name = "Аня" });
        await db.SaveChangesAsync();
        var initial = await owner.ReadAsync();
        var code = await invite.Issue(initial.Id, "anya", default);
        var guestInvite = new InvitationStore(db, new TestUser(101));
        await guestInvite.Accept(code.Token, default);
        var joined = await guest.ReadAsync();
        var latest = await database.Run(s => s.SelectAsync("pizza", "anya", Select(joined)), 101);

        Assert.True(Assert.Single(await owner.ListAsync()).CanDelete);
        Assert.False(Assert.Single(await guest.ListAsync()).CanDelete);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReceiptException>(() => guest.DeleteReceiptAsync(initial.Id, Version(latest)))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() => owner.DeleteReceiptAsync(initial.Id, Version(initial)))).Status);

        var request = new CreateReceiptRequest { RequestId = Guid.NewGuid(), Title = "Кофе", Participants = ["Аня"] };
        var other = await owner.CreateAsync(request);
        await owner.DeleteReceiptAsync(initial.Id, Version(latest));
        Assert.Equal(other.Id, Assert.Single(await owner.ListAsync()).Id);
        Assert.Empty(await guest.ListAsync());
        Assert.Equal(404, (await Assert.ThrowsAsync<ReceiptException>(() => guest.ReadAsync())).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReceiptException>(() => guestInvite.Accept(code.Token, default))).Status);
        Assert.False(await db.Set<AssignmentEntity>().AnyAsync(a => a.ReceiptId == initial.Id));
        Assert.False(await db.Set<ItemEntity>().AnyAsync(i => i.ReceiptId == initial.Id));
        Assert.False(await db.Set<ParticipantEntity>().AnyAsync(p => p.ReceiptId == initial.Id));
        Assert.True(await db.Users.AnyAsync(u => u.Id == 101));

        await owner.DeleteReceiptAsync(other.Id, Version(other));
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() => owner.CreateAsync(request))).Status);
        await using var restarted = database.Context();
        await ReceiptStore.InitializeAsync(restarted);
        Assert.Empty(await new ReceiptStore(restarted, new TestUser(-1)).ListAsync());
    }

    [PostgresFact]
    public async Task OwnerClaimsOneSeatAndOnlyExplicitAdminCanSelectForOthers()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Context();
        var owner = new ReceiptStore(db, new TestUser(-1));
        var old = await owner.ReadAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<ReceiptException>(() =>
            owner.SelectAsync("pizza", "anya", Select(old)))).Status);
        var mine = await owner.IdentifyOwnerAsync("demo", "anya");
        Assert.True(mine.IsOwner);
        Assert.False(mine.CanSelectForOthers);
        Assert.Equal("anya", mine.MyParticipantId);
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() => owner.IdentifyOwnerAsync("demo", "boris"))).Status);
        var selected = await owner.SelectAsync("pizza", "anya", Select(mine));
        Assert.Equal(403, (await Assert.ThrowsAsync<ReceiptException>(() => owner.SelectAsync("pizza", "boris", Select(selected)))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ReceiptException>(() => owner.ResetAsync(Version(selected)))).Status);

        var admin = new ReceiptStore(db, new TestUser(-1, true));
        var ordinary = new SelectionRequest { Selected = true, InstanceId = selected.InstanceId, Revision = selected.Revision };
        Assert.Equal(403, (await Assert.ThrowsAsync<ReceiptException>(() => admin.SelectAsync("pizza", "boris", ordinary))).Status);
        var updated = await admin.SelectAsync("pizza", "boris", Select(selected));
        Assert.Equal(new[] { "anya", "boris" }, updated.Assignments["pizza"]);
        var outsider = new ReceiptStore(db, new TestUser(999, true));
        Assert.Equal(404, (await Assert.ThrowsAsync<ReceiptException>(() => outsider.SelectAsync("pizza", "boris", Select(updated)))).Status);
    }

    private sealed record TestUser(long Id, bool CanSelectForOthers = false) : ICurrentUser;
    private sealed class TestDatabase(string connection, string admin, string name) : IAsyncDisposable
    {
        public ExpensesDbContext Context() => new(new DbContextOptionsBuilder<ExpensesDbContext>().UseNpgsql(connection).Options);
        public async Task<ReceiptDto> Run(Func<ReceiptStore, Task<ReceiptDto>> action, long userId = -1)
        {
            await using var db = Context();
            // Сценарии расчёта выбирают за всю компанию под тестовым администратором.
            return await action(new ReceiptStore(db, new TestUser(userId, CanSelectForOthers: userId == -1)));
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
                        INSERT INTO items ("ReceiptId", "Id", "Name", "Detail", "Amount", "Position", "IsDeleted") VALUES ('demo', 'removed', 'Удалено', '', 10000, 1, true);
                        INSERT INTO assignments ("ReceiptId", "ItemId", "ParticipantId") VALUES ('demo', 'removed', 'anya');
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
