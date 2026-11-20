namespace GeneralExpenses.Api;

public sealed record PersonalLine(string ItemId, string Name, string Share, long Amount);

public static class ReceiptCalculator
{
    // Один расчёт для экрана, подтверждения и сообщения: остаток в копейках
    // в режиме «поровну» получают первые участники в порядке мест чека.
    private static IEnumerable<(string PersonId, PersonalLine Line)> Shares(ReceiptItem item, string[] selected, Participant[] people)
    {
        var owners = people.Where(p => selected.Contains(p.Id)).ToArray();
        for (var i = 0; i < owners.Length; i++)
        {
            var id = owners[i].Id;
            var count = item.Counts?.GetValueOrDefault(id) ?? 0;
            var amount = item.ByPortion ? item.Amount / item.Quantity * count
                : item.Amount / owners.Length + (i < item.Amount % owners.Length ? 1 : 0);
            var share = item.ByPortion ? $"{count} шт." : $"1/{owners.Length}";
            yield return (id, new(item.Id, item.Name, share, amount));
        }
    }

    public static PersonalLine[] Lines(ReceiptDto receipt, string personId) =>
        receipt.Items.SelectMany(item => Shares(item, receipt.Assignments.GetValueOrDefault(item.Id) ?? [], receipt.Participants))
            .Where(s => s.PersonId == personId).Select(s => s.Line).ToArray();

    public static ReceiptTotals Calculate(IEnumerable<ReceiptItem> items, IReadOnlyDictionary<string, string[]> assignments, Participant[] participants)
    {
        var totals = participants.ToDictionary(p => p.Id, _ => 0L);
        long total = 0;
        foreach (var item in items)
        {
            total += item.Amount;
            foreach (var (personId, line) in Shares(item, assignments.GetValueOrDefault(item.Id) ?? [], participants))
                totals[personId] += line.Amount;
        }
        return new(totals, total - totals.Values.Sum(), total);
    }
}
