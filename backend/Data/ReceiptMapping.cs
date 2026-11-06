namespace GeneralExpenses.Api.Data;

public static class ReceiptMapping
{
    public static ReceiptDto ToDto(ReceiptEntity entity)
    {
        var participants = entity.Participants.OrderBy(x => x.Position).Select(x => new Participant(x.Id, x.Name, x.Initial)).ToArray();
        var visible = entity.Items.Where(x => !x.IsDeleted).OrderBy(x => x.Position).ToArray();
        var items = visible.Select(ToItem).ToArray();
        var assignments = visible.ToDictionary(x => x.Id, x => Owners(x, participants));
        var removed = entity.Items.SingleOrDefault(x => x.IsDeleted);
        return new(participants, items, assignments, ReceiptCalculator.Calculate(items, assignments, participants),
            entity.Revision, entity.InstanceId,
            removed is null ? null : new(ToItem(removed), removed.Position, Owners(removed, participants)));
    }

    private static ReceiptItem ToItem(ItemEntity item) => new(item.Id, item.Name, item.Detail, item.Amount);
    private static string[] Owners(ItemEntity item, Participant[] participants) => participants
        .Where(p => item.Assignments.Any(a => a.ParticipantId == p.Id)).Select(p => p.Id).ToArray();

    public static ReceiptEntity Create(ReceiptDto snapshot)
    {
        var entity = new ReceiptEntity { InstanceId = snapshot.InstanceId, Revision = snapshot.Revision };
        entity.Participants = snapshot.Participants.Select((p, index) => new ParticipantEntity
            { Id = p.Id, Name = p.Name, Initial = p.Initial, Position = index }).ToList();
        Apply(entity, snapshot);
        return entity;
    }

    // Меняем отслеживаемые сущности, сохраняя их ключи. EF вычислит необходимые
    // INSERT/UPDATE/DELETE; мы не удаляем и не создаём заново весь чек.
    public static void Apply(ReceiptEntity entity, ReceiptDto snapshot)
    {
        entity.Revision = snapshot.Revision;
        var desired = snapshot.Items.Select((item, index) => (item, index, deleted: false,
            owners: snapshot.Assignments.GetValueOrDefault(item.Id) ?? [])).ToList();
        if (snapshot.Deleted is { } deleted)
            desired.Add((deleted.Item, deleted.Index, true, deleted.Owners));

        entity.Items.RemoveAll(item => desired.All(x => x.item.Id != item.Id));
        foreach (var entry in desired)
        {
            var item = entity.Items.SingleOrDefault(x => x.Id == entry.item.Id);
            if (item is null) { item = new ItemEntity { Id = entry.item.Id }; entity.Items.Add(item); }
            item.Name = entry.item.Name;
            item.Detail = entry.item.Detail;
            item.Amount = entry.item.Amount;
            item.Position = entry.index;
            item.IsDeleted = entry.deleted;
            item.Assignments.RemoveAll(x => !entry.owners.Contains(x.ParticipantId));
            foreach (var owner in entry.owners)
                if (item.Assignments.All(x => x.ParticipantId != owner))
                    item.Assignments.Add(new() { ItemId = item.Id, ParticipantId = owner });
        }
    }
}
