namespace GeneralExpenses.Api.Data;

public static class ReceiptMapping
{
    public static ReceiptDto ToDto(ReceiptEntity entity)
    {
        var participants = entity.Participants.OrderBy(x => x.Position).Select(x => new Participant(x.Id, x.Name, x.Initial, x.UserId.HasValue, x.Finished)).ToArray();
        var ordered = entity.Items.OrderBy(x => x.Position).ToArray();
        var items = ordered.Select(ToItem).ToArray();
        var assignments = ordered.ToDictionary(x => x.Id, x => Owners(x, participants));
        return new(participants, items, assignments, ReceiptCalculator.Calculate(items, assignments, participants),
            entity.Revision, entity.InstanceId, entity.Id, entity.Title, entity.CreatedAt);
    }

    private static ReceiptItem ToItem(ItemEntity item) => new(item.Id, item.Name, item.Detail, item.Amount, item.Quantity,
        item.ByPortion, item.ByPortion ? item.Assignments.ToDictionary(a => a.ParticipantId, a => a.Count) : null);
    private static string[] Owners(ItemEntity item, Participant[] participants) => participants
        .Where(p => item.Assignments.Any(a => a.ParticipantId == p.Id)).Select(p => p.Id).ToArray();

    public static ReceiptEntity Create(ReceiptDto snapshot)
    {
        var entity = new ReceiptEntity { Id = snapshot.Id, Title = snapshot.Title, CreatedAt = snapshot.CreatedAt == default ? DateTimeOffset.UtcNow : snapshot.CreatedAt, InstanceId = snapshot.InstanceId, Revision = snapshot.Revision };
        entity.Participants = snapshot.Participants.Select((p, index) => new ParticipantEntity
            { ReceiptId = entity.Id, Id = p.Id, Name = p.Name, Initial = p.Initial, Position = index }).ToList();
        Apply(entity, snapshot);
        return entity;
    }

    // Меняем отслеживаемые сущности, сохраняя их ключи. EF вычислит необходимые
    // INSERT/UPDATE/DELETE; мы не удаляем и не создаём заново весь чек.
    public static void Apply(ReceiptEntity entity, ReceiptDto snapshot)
    {
        entity.Revision = snapshot.Revision;
        var desired = snapshot.Items.Select((item, index) => (item, index,
            owners: snapshot.Assignments.GetValueOrDefault(item.Id) ?? [])).ToList();

        entity.Items.RemoveAll(item => desired.All(x => x.item.Id != item.Id));
        foreach (var entry in desired)
        {
            var item = entity.Items.SingleOrDefault(x => x.Id == entry.item.Id);
            if (item is null) { item = new ItemEntity { ReceiptId = entity.Id, Id = entry.item.Id }; entity.Items.Add(item); }
            item.Name = entry.item.Name;
            item.Detail = entry.item.Detail;
            item.Amount = entry.item.Amount;
            item.Quantity = entry.item.Quantity;
            item.ByPortion = entry.item.ByPortion;
            item.Position = entry.index;
            item.Assignments.RemoveAll(x => !entry.owners.Contains(x.ParticipantId));
            foreach (var owner in entry.owners)
            {
                var assignment = item.Assignments.SingleOrDefault(x => x.ParticipantId == owner);
                if (assignment is null)
                {
                    assignment = new() { ReceiptId = entity.Id, ItemId = item.Id, ParticipantId = owner };
                    item.Assignments.Add(assignment);
                }
                assignment.Count = entry.item.Counts?.GetValueOrDefault(owner) ?? 1;
            }
        }
    }
}
