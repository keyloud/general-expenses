using GeneralExpenses.Api;
using GeneralExpenses.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
// Локальная разработка пишет логи в терминал, без доступа к журналу событий Windows.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddControllers();
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
app.MapControllers();
app.Run();
