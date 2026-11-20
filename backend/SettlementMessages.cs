using System.Globalization;
using System.Text;

namespace GeneralExpenses.Api;

public static class SettlementMessages
{
    private static string Money(long kopecks) => (kopecks / 100m).ToString("0.00", CultureInfo.GetCultureInfo("ru-RU")) + " ₽";

    public static IEnumerable<string> ForAdmin(ReceiptDto receipt, string[] personIds)
    {
        var header = $"Расчёт по счёту «{receipt.Title}»";
        var message = new StringBuilder(header);
        foreach (var person in receipt.Participants.Where(p => personIds.Contains(p.Id)))
        {
            var heading = $"\n\n{person.Name} — {Money(receipt.Result.Totals[person.Id])}";
            if (message.Length + heading.Length > 3900)
            {
                yield return message.ToString();
                message = new StringBuilder(header);
            }
            message.Append(heading);
            foreach (var line in ReceiptCalculator.Lines(receipt, person.Id))
            {
                var text = $"\n• {line.Name} · {line.Share} — {Money(line.Amount)}";
                if (message.Length + text.Length > 3900)
                {
                    yield return message.ToString();
                    message = new StringBuilder(header).Append(heading);
                }
                message.Append(text);
            }
        }
        var total = personIds.Sum(id => receipt.Result.Totals[id]);
        yield return message.Append($"\n\nИтого по участникам: {Money(total)}").ToString();
    }

    public static IEnumerable<string> For(ReceiptDto receipt, string personId, bool owner)
    {
        var header = $"Итог по счёту «{receipt.Title}»";
        var message = new StringBuilder(header);
        var lines = ReceiptCalculator.Lines(receipt, personId);
        foreach (var line in lines)
        {
            var text = $"\n{line.Name} · {line.Share} — {Money(line.Amount)}";
            // Telegram принимает до 4096 символов. Переносим только целые строки.
            if (message.Length + text.Length > 3900)
            {
                yield return message.ToString();
                message = new StringBuilder(header);
            }
            message.Append(text);
        }
        if (lines.Length == 0) message.Append("\nВы не выбирали позиций.");
        var label = owner ? "Ваша доля" : "Итого к переводу";
        yield return message.Append($"\n\n{label}: {Money(receipt.Result.Totals[personId])}").ToString();
    }
}
