namespace GeneralExpenses.Api;

public sealed class ReceiptState
{
    private readonly string instanceId = Guid.NewGuid().ToString();
    private long revision;
    private readonly string receiptId = "demo";
    private readonly string title = "Ужин с друзьями";
    private readonly DateTimeOffset createdAt = DateTimeOffset.UtcNow;
    private readonly Participant[] participants = [new("anya", "Аня", "А"), new("boris", "Борис", "Б"), new("vika", "Вика", "В")];
    private readonly List<ReceiptItem> items = [
        new("pizza", "Пицца «Маргарита»", "1 пицца · можно разделить", 90000),
        new("pasta", "Паста с грибами", "1 порция", 65000),
        new("dessert", "Тирамису", "1 порция · можно разделить", 35000),
        new("drink1", "Домашний лимонад", "Стакан 1 из 3 · 300 мл", 20000),
        new("drink2", "Домашний лимонад", "Стакан 2 из 3 · 300 мл", 20000),
        new("drink3", "Домашний лимонад", "Стакан 3 из 3 · 300 мл", 20000)
    ];
    private readonly Dictionary<string, string[]> assignments = [];

    public ReceiptState() { }

    // Восстанавливаем бизнес-состояние из БД. Версия и идентификатор живут вместе с чеком.
    public ReceiptState(ReceiptDto snapshot)
    {
        receiptId = snapshot.Id;
        title = snapshot.Title;
        createdAt = snapshot.CreatedAt;
        instanceId = snapshot.InstanceId;
        revision = snapshot.Revision;
        participants = snapshot.Participants.ToArray();
        items.Clear();
        items.AddRange(snapshot.Items);
        foreach (var pair in snapshot.Assignments) assignments[pair.Key] = pair.Value.ToArray();
    }

    public ReceiptDto Read() => Snapshot();

    // Проверяем версию загруженного состояния. Транзакцией и блокировкой строки БД
    // управляет ReceiptStore; этот класс отвечает только за правила чека.
    private ReceiptDto Change(ChangeRequest request, Action action)
    {
        // Изолированный экземпляр состояния создаётся для каждой операции.
        if (request.InstanceId != instanceId || request.Revision != revision)
            throw new ReceiptException(409, "Чек изменился. Обновите его и повторите действие.");
        action();
        revision++;
        return Snapshot();
    }

    private int Index(string id)
    {
        var index = items.FindIndex(item => item.Id == id);
        return index >= 0 ? index : throw new ReceiptException(404, "Позиция больше не существует.");
    }

    private static void Validate(ItemRequest request)
    {
        // Проверка на сервере обязательна: клиентскую форму можно обойти прямым HTTP-запросом.
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100 || request.Amount is null or < 1 or > 100_000_000)
            throw new ReceiptException(400, "Введите название до 100 символов и цену от 0,01 до 1 000 000 ₽.");
    }

    public ReceiptDto Add(ItemRequest request) => Change(request, () =>
    {
        Validate(request);
        items.Add(UpdatedItem(new(Guid.NewGuid().ToString(), "", "", 0), request));
    });
    public ReceiptDto Edit(string id, ItemRequest request) => Change(request, () =>
    {
        Validate(request);
        var index = Index(id);
        items[index] = UpdatedItem(items[index], request);
    });

    private ReceiptItem UpdatedItem(ReceiptItem item, ItemRequest request)
    {
        var quantity = request.Quantity ?? item.Quantity;
        var byPortion = request.ByPortion ?? item.ByPortion;
        var amount = request.Amount!.Value;
        if (quantity is < 1 or > 999 || amount % quantity != 0)
            throw new ReceiptException(400, "Укажите количество от 1 до 999 и цену за единицу в целых копейках.");
        var counts = byPortion ? item.Counts : null;
        if (byPortion && !item.ByPortion && assignments.GetValueOrDefault(item.Id) is { Length: > 0 } owners)
        {
            if (quantity < owners.Length)
                throw new ReceiptException(400, $"Позицию выбрали {owners.Length} чел. Для перехода на целые порции укажите количество не меньше {owners.Length}.");
            // Все выбранные места сохраняются; лишние целые порции получают первые по порядку в чеке.
            counts = new Dictionary<string, int>();
            var index = 0;
            foreach (var person in participants.Where(p => owners.Contains(p.Id)))
                counts[person.Id] = quantity / owners.Length + (index++ < quantity % owners.Length ? 1 : 0);
        }
        if (quantity < (counts?.Values.Sum() ?? 0))
            throw new ReceiptException(409, "Часть этих порций уже выбрана.");
        return item with { Name = request.Name!.Trim(), Amount = amount, Quantity = quantity, ByPortion = byPortion, Counts = counts,
            Detail = quantity == item.Quantity && item.Detail.Length > 0 ? item.Detail : $"{quantity} шт." };
    }
    public ReceiptDto Delete(string id, ChangeRequest request) => Change(request, () =>
    {
        var index = Index(id);
        items.RemoveAt(index);
        assignments.Remove(id);
    });
    public ReceiptDto Select(string id, string personId, SelectionRequest request) => Change(request, () =>
    {
        var index = Index(id);
        var item = items[index];
        if (!participants.Any(p => p.Id == personId)) throw new ReceiptException(400, "Неизвестный участник.");
        var owners = (assignments.GetValueOrDefault(id) ?? []).ToHashSet();
        // Передаём желаемое состояние, а не команду toggle: смысл запроса однозначен.
        if (item.ByPortion)
        {
            if (request.Count is not int count || count < 0 || count > item.Quantity || request.Selected != (count > 0))
                throw new ReceiptException(400, "Укажите количество выбранных порций.");
            var counts = new Dictionary<string, int>(item.Counts ?? []);
            if (count == 0) counts.Remove(personId); else counts[personId] = count;
            if (counts.Values.Sum() > item.Quantity) throw new ReceiptException(409, "Свободных порций уже нет.");
            items[index] = item with { Counts = counts };
            owners = counts.Keys.ToHashSet();
        }
        else
        {
            if (request.Count.HasValue) throw new ReceiptException(400, "Эта позиция делится поровну.");
            if (request.Selected == true) owners.Add(personId); else owners.Remove(personId);
        }
        assignments[id] = participants.Where(p => owners.Contains(p.Id)).Select(p => p.Id).ToArray();
    });
    public ReceiptDto Reset(ChangeRequest request) => Change(request, () =>
    {
        assignments.Clear();
        for (int i = 0; i < items.Count; i++) items[i] = items[i] with { Counts = null };
    });

    // Отдаём снимок состояния, не раскрывая изменяемые коллекции бизнес-логики.
    private ReceiptDto Snapshot() => new(participants.ToArray(), items.ToArray(),
        assignments.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()),
        ReceiptCalculator.Calculate(items, assignments, participants), revision, instanceId,
        receiptId, title, createdAt);
}
