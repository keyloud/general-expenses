namespace GeneralExpenses.Api;

public static class ReceiptCalculator
{
    // Чистая функция: не знает ни о HTTP, ни о хранилище и не меняет входные данные.
    public static ReceiptTotals Calculate(IEnumerable<ReceiptItem> items,
        IReadOnlyDictionary<string, string[]> assignments, Participant[] participants)
    {
        var totals = participants.ToDictionary(p => p.Id, _ => 0L);
        long unassigned = 0, total = 0;
        foreach (var item in items)
        {
            total += item.Amount;
            var selected = assignments.GetValueOrDefault(item.Id) ?? [];
            var owners = participants.Where(p => selected.Contains(p.Id)).ToArray();
            if (owners.Length == 0) { unassigned += item.Amount; continue; }
            var share = item.Amount / owners.Length;
            var remainder = item.Amount % owners.Length;
            // Остаток раздаётся по копейке в стабильном порядке участников.
            // Поэтому сумма долей всегда точно равна цене блюда.
            for (var i = 0; i < owners.Length; i++)
                totals[owners[i].Id] += share + (i < remainder ? 1 : 0);
        }
        return new(totals, unassigned, total);
    }
}
