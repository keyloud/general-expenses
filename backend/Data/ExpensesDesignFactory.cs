using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GeneralExpenses.Api.Data;

// Создание миграций не должно запускать веб-сервер и начальное заполнение.
public sealed class ExpensesDesignFactory : IDesignTimeDbContextFactory<ExpensesDbContext>
{
    public ExpensesDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Expenses")
            ?? "Host=127.0.0.1;Port=55432;Database=general_expenses;Username=expenses";
        return new(new DbContextOptionsBuilder<ExpensesDbContext>().UseNpgsql(connection).Options);
    }
}
