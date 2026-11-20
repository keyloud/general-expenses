using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeneralExpenses.Api;
using GeneralExpenses.Api.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed partial class PostgresTests
{
    [PostgresFact]
    public async Task OnlyOwnerCanPrepareActiveInvitationForPrivateUserChats()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Context();
        db.Users.Add(new GeneralExpenses.Api.Data.UserEntity { Id = 101, Name = "Автор" });
        await db.SaveChangesAsync();
        var owner = new TestUser(101);
        var receipt = await new ReceiptStore(db, owner).CreateAsync(new()
            { RequestId = Guid.NewGuid(), Title = "Кофе", Participants = ["Аня"] });
        var invites = new InvitationStore(db, owner);
        var code = await invites.Issue(receipt.Id, receipt.Participants.Single().Id, default);
        var guest = new InvitationStore(db, new TestUser(202));
        Assert.Equal(404, (await Assert.ThrowsAsync<ReceiptException>(() => guest.ReadForSharing(code.Token, default))).Status);

        var transport = new PrepareTransport();
        var services = new ServiceCollection();
        services.AddHttpClient("TelegramBot").ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Telegram:BotToken"] = "test-only" }).Build();
        var share = new TelegramInvitationShare(provider.GetRequiredService<IHttpClientFactory>(), config, owner);
        var invitation = await invites.ReadForSharing(code.Token, default);
        Assert.Equal("prepared-id", (await share.Prepare(code.Token, invitation, default)).Id);
        var body = transport.Payload;
        Assert.Equal(101, body.GetProperty("user_id").GetInt64());
        Assert.True(body.GetProperty("allow_user_chats").GetBoolean());
        foreach (var flag in new[] { "allow_bot_chats", "allow_group_chats", "allow_channel_chats" })
            Assert.False(body.GetProperty(flag).GetBoolean());
        var result = body.GetProperty("result");
        Assert.Contains("Кофе", result.GetProperty("input_message_content").GetProperty("message_text").GetString());
        Assert.Equal("https://t.me/porovnu_expenses_bot?start=invite_" + code.Token,
            result.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0].GetProperty("url").GetString());
        Assert.Equal(invitation, await invites.ReadForSharing(code.Token, default));
        await invites.Issue(receipt.Id, receipt.Participants.Single().Id, default);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReceiptException>(() => invites.ReadForSharing(code.Token, default))).Status);
    }

    private sealed class PrepareTransport : HttpMessageHandler
    {
        public JsonElement Payload { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.EndsWith("/savePreparedInlineMessage", request.RequestUri!.AbsolutePath);
            Payload = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { ok = true, result = new { id = "prepared-id" } }) };
        }
    }

    [PostgresFact]
    public async Task StartInPrivateChatAddsReceiptBeforeFirstMiniAppLogin()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Context();
        var code = await new InvitationStore(db, new TestUser(-1)).Issue("demo", "anya", default);
        var telegram = new ChatTransport(code.Token);
        var services = new ServiceCollection();
        services.AddScoped(_ => database.Context());
        services.AddScoped<ICurrentUser>(_ => new CurrentUser(new Microsoft.AspNetCore.Http.HttpContextAccessor(), new ConfigurationBuilder().Build()));
        services.AddScoped<InvitationStore>();
        services.AddHttpClient("TelegramBot").ConfigurePrimaryHttpMessageHandler(() => telegram);
        await using var provider = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Telegram:BotToken"] = "test-only", ["Telegram:MiniAppUrl"] = "https://app.example/"
        }).Build();
        using var bot = new TelegramChatBot(provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IServiceScopeFactory>(), config, NullLogger<TelegramChatBot>.Instance);
        await bot.StartAsync(default);
        try { await telegram.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { await bot.StopAsync(default); }
        Assert.Equal(3, telegram.Replies.Count); // Групповой /start проигнорирован.
        foreach (var reply in telegram.Replies.Take(2))
        {
            Assert.Equal(101, reply.GetProperty("chat_id").GetInt64());
            Assert.Contains("Ужин с друзьями", reply.GetProperty("text").GetString());
            var button = reply.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0];
            Assert.Equal("Открыть чек", button.GetProperty("text").GetString());
            Assert.Equal("https://app.example/?receipt=demo", button.GetProperty("web_app").GetProperty("url").GetString());
        }
        Assert.DoesNotContain("Ужин с друзьями", telegram.Replies[2].GetProperty("text").GetString());
        Assert.False(await db.Users.AnyAsync(u => u.Id == 202));
        var receipt = await database.Run(s => s.ReadAsync(), 101);
        Assert.Equal("anya", receipt.MyParticipantId);
        Assert.Equal(1, receipt.Revision);
    }

    // Подменяем только сеть Telegram. Бот, транзакции и список счетов настоящие.
    private sealed class ChatTransport(string token) : HttpMessageHandler
    {
        public List<JsonElement> Replies { get; } = [];
        public TaskCompletionSource Acknowledged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            if (request.RequestUri!.AbsolutePath.EndsWith("/sendMessage"))
            {
                Replies.Add(body);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { ok = true }) };
            }
            if (body.GetProperty("offset").GetInt64() == 0)
            {
                var updates = new[] { (1, 101, "group"), (2, 101, "private"), (3, 101, "private"), (4, 202, "private") };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new {
                    ok = true, result = updates.Select(u => new {
                        update_id = u.Item1, message = new {
                            text = "/start invite_" + token, chat = new { id = u.Item2, type = u.Item3 },
                            from = new { id = u.Item2, first_name = "Тест", is_bot = false }
                        }
                    })
                }) };
            }
            if (body.GetProperty("offset").GetInt64() == 5) Acknowledged.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new OperationCanceledException(ct);
        }
    }
}
