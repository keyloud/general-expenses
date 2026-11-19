using GeneralExpenses.Api.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using GeneralExpenses.Api;
using GeneralExpenses.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
// Локальная разработка пишет логи в терминал, без доступа к журналу событий Windows.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "porovnu.session";
    options.Cookie.HttpOnly = true;
    // Telegram Web может открывать Mini App во вложенном окне другого сайта.
    // Для HTTPS разрешаем такую cookie; изменения дополнительно защищены заголовком ниже.
    options.Cookie.SameSite = builder.Configuration.GetValue<bool>("Auth:AllowHttp")
        ? SameSiteMode.Strict : SameSiteMode.None;
    options.Cookie.SecurePolicy = builder.Configuration.GetValue<bool>("Auth:AllowHttp")
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    // API возвращает статус, а не HTML-перенаправление на страницу входа.
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(options => options.FallbackPolicy =
    new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
var protection = builder.Services.AddDataProtection().SetApplicationName("Porovnu");
if (builder.Configuration["Auth:KeysPath"] is { Length: > 0 } keysPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
// Строка подключения передаётся окружением; пароль не попадает в репозиторий.
var connection = builder.Configuration.GetConnectionString("Expenses")
    ?? throw new InvalidOperationException("Задайте ConnectionStrings__Expenses или запустите scripts/Start-Backend.ps1.");
builder.Services.AddDbContext<ExpensesDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<ReceiptStore>();
builder.Services.AddProblemDetails();
var app = builder.Build();
// Миграции выполняются явной командой, а не при каждом HTTP-запуске.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
    if (args.Contains("--migrate"))
    {
        await db.Database.MigrateAsync();
        return;
    }
    var importIndex = Array.IndexOf(args, "--import-receipt");
    ReceiptDto? import = null;
    if (importIndex >= 0)
    {
        if (importIndex + 1 >= args.Length) throw new ArgumentException("Укажите путь к JSON чека.");
        import = JsonSerializer.Deserialize<ReceiptDto>(await File.ReadAllTextAsync(args[importIndex + 1]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new ArgumentException("Пустой JSON чека.");
    }
    await ReceiptStore.InitializeAsync(db, import);
    if (importIndex >= 0) return;
}
app.UseExceptionHandler();
// Браузер не может отправить этот заголовок с чужого сайта без разрешения CORS.
// CORS не включён: интерфейс и API обслуживаются с одного origin (в dev — proxy Vite).
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) &&
            context.Request.Headers["X-Requested-With"] != "Porovnu")
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { title = "Запрос должен поступать из приложения." });
            return;
        }
    }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
