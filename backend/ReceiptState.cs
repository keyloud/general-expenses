namespace GeneralExpenses.Api;

public sealed class ReceiptState
{
    private readonly string instanceId = Guid.NewGuid().ToString();
    private long revision;
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
    private DeletedItem? deleted;

    public ReceiptState() { }

    // Восстанавливаем бизнес-состояние из БД. Версия и идентификатор живут вместе с чеком.
    public ReceiptState(ReceiptDto snapshot)
    {
        instanceId = snapshot.InstanceId;
        revision = snapshot.Revision;
        participants = snapshot.Participants.ToArray();
        items.Clear();
        items.AddRange(snapshot.Items);
        foreach (var pair in snapshot.Assignments) assignments[pair.Key] = pair.Value.ToArray();
        deleted = snapshot.Deleted;
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
        items.Add(new(Guid.NewGuid().ToString(), request.Name!.Trim(), "1 порция · добавлено вручную", request.Amount!.Value));
    });
    public ReceiptDto Edit(string id, ItemRequest request) => Change(request, () =>
    {
        Validate(request);
        var index = Index(id);
        items[index] = items[index] with { Name = request.Name!.Trim(), Amount = request.Amount!.Value };
    });
    public ReceiptDto Delete(string id, ChangeRequest request) => Change(request, () =>
    {
        var index = Index(id);
        deleted = new(items[index], index, assignments.GetValueOrDefault(id) ?? []);
        items.RemoveAt(index);
        assignments.Remove(id);
    });
    public ReceiptDto Undo(ChangeRequest request) => Change(request, () =>
    {
        if (deleted is null) throw new ReceiptException(404, "Нет удаления для отмены.");
        items.Insert(Math.Min(deleted.Index, items.Count), deleted.Item);
        assignments[deleted.Item.Id] = deleted.Owners;
        deleted = null;
    });
    public ReceiptDto Select(string id, string personId, SelectionRequest request) => Change(request, () =>
    {
        Index(id);
        if (!participants.Any(p => p.Id == personId)) throw new ReceiptException(400, "Неизвестный участник.");
        var owners = (assignments.GetValueOrDefault(id) ?? []).ToHashSet();
        // Передаём желаемое состояние, а не команду toggle: смысл запроса однозначен.
        if (request.Selected == true) owners.Add(personId); else owners.Remove(personId);
        assignments[id] = participants.Where(p => owners.Contains(p.Id)).Select(p => p.Id).ToArray();
    });
    public ReceiptDto Reset(ChangeRequest request) => Change(request, () =>
    {
        assignments.Clear();
        if (deleted is not null) deleted = deleted with { Owners = [] };
    });

    // Отдаём снимок состояния, не раскрывая изменяемые коллекции бизнес-логики.
    private ReceiptDto Snapshot() => new(participants.ToArray(), items.ToArray(),
        assignments.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()),
        ReceiptCalculator.Calculate(items, assignments, participants), revision, instanceId,
        deleted is null ? null : deleted with { Owners = deleted.Owners.ToArray() });
}

