using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeneralExpenses.Api;
using GeneralExpenses.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed partial class PostgresTests
{
    [PostgresFact]
    public async Task AdminCanContinueAfterOwnConfirmationAndReceiveOneCombinedSettlement()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Context();
        db.Users.AddRange(new UserEntity { Id = 101, Name = "Автор" }, new UserEntity { Id = 202, Name = "Борис" });
        await db.SaveChangesAsync();
        async Task<ReceiptDto> AsAdmin(Func<ReceiptStore, Task<ReceiptDto>> action)
        {
            await using var context = database.Context();
            return await action(new ReceiptStore(context, new TestUser(101, true)));
        }

        var receipt = await AsAdmin(s => s.CreateAsync(new() { RequestId = Guid.NewGuid(), Title = "Бар",
            Participants = ["Автор", "Борис", "Вика"] }));
        var id = receipt.Id;
        var people = receipt.Participants;
        receipt = await AsAdmin(s => s.IdentifyOwnerAsync(id, people[0].Id));
        var invite = await new InvitationStore(db, new TestUser(101)).Issue(id, people[1].Id, default);
        await new InvitationStore(db, new TestUser(202)).Accept(invite.Token, default);
        receipt = await AsAdmin(s => s.ReadAsync(receiptId: id));
        receipt = await AsAdmin(s => s.AddAsync(Item(receipt, "Пиво", 50000), receiptId: id));
        var beer = receipt.Items.Single().Id;
        receipt = await AsAdmin(s => s.FinishAsync(id, Version(receipt)));
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() => AsAdmin(s =>
            s.AdminConfirmAsync(id, null, Version(receipt), true)))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ReceiptException>(() => database.Run(s =>
            s.AdminConfirmAsync(id, null, Version(receipt), true), 202))).Status);
        receipt = await AsAdmin(s => s.SelectAsync(beer, people[1].Id, Select(receipt), receiptId: id));
        receipt = await AsAdmin(s => s.AdminConfirmAsync(id, people[1].Id, Version(receipt), true));
        Assert.True(receipt.Participants[1].Finished);
        receipt = await AsAdmin(s => s.AdminConfirmAsync(id, people[1].Id, Version(receipt), false));
        Assert.False(receipt.Participants[1].Finished);
        Assert.True(receipt.Participants[0].Finished);
        var before = receipt;
        receipt = await AsAdmin(s => s.AdminConfirmAsync(id, null, Version(before), true));
        Assert.True(receipt.IsCompleted);
        await AsAdmin(s => s.AdminConfirmAsync(id, null, Version(before), true));
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() => AsAdmin(s =>
            s.AdminConfirmAsync(id, people[1].Id, Version(receipt), false)))).Status);
        var message = Assert.Single(await db.Messages.AsNoTracking().ToArrayAsync());
        Assert.Equal(101, message.ChatId);
        Assert.Contains("Автор — 0,00", message.Text);
        Assert.Contains("Борис — 500,00", message.Text);
        Assert.Contains("Пиво · 1/1 части", message.Text);
        Assert.Contains("Вика — 0,00", message.Text);
        Assert.Contains("Итого по участникам: 500,00", message.Text);
    }

    [PostgresFact]
    public async Task SelfConfirmedParticipantGetsOwnMessageWhenAdminConfirmsOtherSeats()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Context();
        db.Users.AddRange(new UserEntity { Id = 101, Name = "Админ" }, new UserEntity { Id = 202, Name = "Аня" });
        await db.SaveChangesAsync();
        var admin = new ReceiptStore(db, new TestUser(101, true));
        var receipt = await admin.CreateAsync(new() { RequestId = Guid.NewGuid(), Title = "Обед", Participants = ["Аня", "Ира"] });
        var id = receipt.Id;
        var a = receipt.Participants[0].Id;
        var b = receipt.Participants[1].Id;
        var code = await new InvitationStore(db, new TestUser(101)).Issue(id, a, default);
        await new InvitationStore(db, new TestUser(202)).Accept(code.Token, default);
        receipt = await admin.ReadAsync(receiptId: id);
        receipt = await admin.AddAsync(Item(receipt, "Суп", 40000), receiptId: id);
        receipt = await admin.SelectAsync(receipt.Items.Single().Id, b, Select(receipt), receiptId: id);
        receipt = await admin.AdminConfirmAsync(id, b, Version(receipt), true);
        receipt = await database.Run(s => s.FinishAsync(id, Version(receipt)), 202);
        Assert.True(receipt.IsCompleted);
        var messages = await db.Messages.AsNoTracking().ToDictionaryAsync(m => m.ChatId);
        Assert.Contains("Ира — 400,00", messages[101].Text);
        Assert.DoesNotContain("Аня", messages[101].Text);
        Assert.Contains("Итого к переводу: 0,00", messages[202].Text);
    }

    [PostgresFact]
    public async Task WholePortionsConfirmationsAndPrivateNotifications()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Context();
        db.Users.AddRange(new UserEntity { Id = 101, Name = "Аня", Username = "owner" },
            new UserEntity { Id = 202, Name = "Борис" });
        await db.SaveChangesAsync();
        var owner = new ReceiptStore(db, new TestUser(101));
        var receipt = await owner.CreateAsync(new() { RequestId = Guid.NewGuid(), Title = "Бар", Participants = ["Аня", "Борис"] });
        var id = receipt.Id;
        var a = receipt.Participants[0].Id;
        var b = receipt.Participants[1].Id;
        await owner.IdentifyOwnerAsync(id, a);
        var invite = await new InvitationStore(db, new TestUser(101)).Issue(id, b, default);
        await new InvitationStore(db, new TestUser(202)).Accept(invite.Token, default);
        receipt = await database.Run(s => s.ReadAsync(receiptId: id), 101);
        receipt = await database.Run(s => s.AddAsync(new ItemRequest { Name = "Пиво", Amount = 90000, Quantity = 3,
            ByPortion = true, InstanceId = receipt.InstanceId, Revision = receipt.Revision }, receiptId: id), 101);
        var beer = receipt.Items.Single().Id;
        receipt = await database.Run(s => s.AddAsync(Item(receipt, "Пицца", 60001), receiptId: id), 101);
        var pizza = receipt.Items.Last().Id;
        SelectionRequest Count(int n) => new() { Selected = n > 0, Count = n, Revision = receipt.Revision, InstanceId = receipt.InstanceId };
        receipt = await database.Run(s => s.SelectAsync(beer, a, Count(2), receiptId: id), 101);
        Assert.Equal(60000, receipt.Result.Totals[a]);
        Assert.Equal(90001, receipt.Result.Unassigned);
        receipt = await database.Run(s => s.FinishAsync(id, Version(receipt)), 101);
        Assert.True(receipt.Participants.Single(p => p.Id == a).Finished);
        Assert.Empty(await db.Messages.ToArrayAsync());
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() =>
            database.Run(s => s.FinishAsync(id, Version(receipt)), 202))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() =>
            database.Run(s => s.AddAsync(Item(receipt, "Суп", 15000), receiptId: id), 101))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() =>
            database.Run(s => s.SelectAsync(beer, b, Count(2), receiptId: id), 202))).Status);
        receipt = await database.Run(s => s.SelectAsync(beer, b, Count(1), receiptId: id), 202);
        receipt = await database.Run(s => s.SelectAsync(pizza, b, Select(receipt), receiptId: id), 202);
        Assert.Equal(0, receipt.Result.Unassigned);
        Assert.True(receipt.Participants.Single(p => p.Id == a).Finished);
        Assert.Empty(await db.Messages.ToArrayAsync());
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() =>
            database.Run(s => s.SelectAsync(pizza, a, Select(receipt), receiptId: id), 101))).Status);
        receipt = await database.Run(s => s.ReopenAsync(id, Version(receipt)), 101);
        Assert.False(receipt.Participants.Single(p => p.Id == a).Finished);
        Assert.Equal(60000, receipt.Result.Totals[a]);
        receipt = await database.Run(s => s.SelectAsync(pizza, a, Select(receipt), receiptId: id), 101);
        Assert.False(receipt.Participants.Single(p => p.Id == a).Finished);
        receipt = await database.Run(s => s.FinishAsync(id, Version(receipt)), 202);
        Assert.Empty(await db.Messages.ToArrayAsync());
        var final = await database.Run(s => s.FinishAsync(id, Version(receipt)), 101);
        Assert.True(final.IsCompleted);
        Assert.True((await database.Run(s => s.ReadAsync(receiptId: id), 202)).IsCompleted);
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() =>
            database.Run(s => s.ReopenAsync(id, Version(final)), 101))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() =>
            database.Run(s => s.ReopenAsync(id, Version(final)), 202))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() =>
            database.Run(s => s.DeleteAsync(beer, Version(final), receiptId: id), 101))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ReceiptException>(() => owner.DeleteReceiptAsync(id, Version(final)))).Status);
        await database.Run(s => s.FinishAsync(id, Version(receipt)), 101); // Повтор после потери ответа.
        Assert.Equal(90001, final.Result.Totals[a]);
        Assert.Equal(60000, final.Result.Totals[b]);
        var messages = await db.Messages.AsNoTracking().OrderBy(m => m.ChatId).ToArrayAsync();
        Assert.Equal(2, messages.Length);
        Assert.Contains("Пиво · 2 шт.", messages[0].Text);
        Assert.Contains("Пицца · 1/2 части", messages[0].Text);
        Assert.Contains("Ваша доля: 900,01", messages[0].Text);
        Assert.Contains("Итого к переводу: 600,00", messages[1].Text);
        Assert.Equal("https://t.me/owner", messages[1].OwnerLink);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using var worker = ActivatorUtilities.CreateInstance<SettlementSender>(provider);
        var network = new FakeSettlementTelegram();
        using var client = new HttpClient(network) { BaseAddress = new Uri("https://test.invalid/") };
        while (await worker.DeliverNext(db, client, default)) { }
        Assert.Equal(new long[] { 101, 202 }, network.ChatIds.Order());
        Assert.Empty(await db.Messages.ToArrayAsync());
    }

    private sealed class FakeSettlementTelegram : HttpMessageHandler
    {
        public List<long> ChatIds { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/sendMessage", request.RequestUri!.AbsolutePath);
            var payload = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal("https://t.me/owner", payload.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0].GetProperty("url").GetString());
            ChatIds.Add(payload.GetProperty("chat_id").GetInt64());
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
