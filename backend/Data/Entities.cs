namespace GeneralExpenses.Api.Data;

// Сущности БД отделены от DTO: они описывают строки таблиц и отношения между ними.
public sealed class ReceiptEntity
{
    public string Id { get; set; } = "demo";
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();
    public long Revision { get; set; }
    public List<ParticipantEntity> Participants { get; set; } = [];
    public List<ItemEntity> Items { get; set; } = [];
}
public sealed class ParticipantEntity
{
    public string ReceiptId { get; set; } = "demo";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initial { get; set; } = "";
    public int Position { get; set; }
}
public sealed class ItemEntity
{
    public string ReceiptId { get; set; } = "demo";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Detail { get; set; } = "";
    public long Amount { get; set; }
    public int Position { get; set; }
    // Последнюю удалённую позицию сохраняем для отмены, но исключаем из расчёта.
    public bool IsDeleted { get; set; }
    public List<AssignmentEntity> Assignments { get; set; } = [];
}
public sealed class AssignmentEntity
{
    public string ReceiptId { get; set; } = "demo";
    public string ItemId { get; set; } = "";
    public string ParticipantId { get; set; } = "";
}
