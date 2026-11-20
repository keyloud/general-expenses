namespace GeneralExpenses.Api.Data;

// Сущности БД отделены от DTO: они описывают строки таблиц и отношения между ними.
// Telegram ID помещается в long. -1 зарезервирован за локальным учебным аккаунтом.
public sealed class UserEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Username { get; set; }
}
public sealed class ReceiptEntity
{
    public string Id { get; set; } = "demo";
    public long OwnerUserId { get; set; } = -1;
    public string Title { get; set; } = "Ужин с друзьями";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();
    public long Revision { get; set; }
    public List<ParticipantEntity> Participants { get; set; } = [];
    public List<ItemEntity> Items { get; set; } = [];
}
// Храним только ID, чтобы повтор старого запроса создания не воскресил удалённый чек.
public sealed class DeletedReceiptEntity
{
    public required string Id { get; init; }
}

public sealed class ParticipantEntity
{
    public bool Finished { get; set; }
    // При подтверждении за участника итог получает администратор из серверной сессии.
    public long? ConfirmedByAdminId { get; set; }
    // Имя для расчёта существует до того, как участник примет приглашение.
    public long? UserId { get; set; }
    public string? InviteHash { get; set; }
    public DateTimeOffset? InviteExpiresAt { get; set; }
    public string ReceiptId { get; set; } = "demo";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initial { get; set; } = "";
    public int Position { get; set; }
}
public sealed class ItemEntity
{
    public int Quantity { get; set; } = 1;
    public bool ByPortion { get; set; }
    public string ReceiptId { get; set; } = "demo";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Detail { get; set; } = "";
    public long Amount { get; set; }
    public int Position { get; set; }
    public List<AssignmentEntity> Assignments { get; set; } = [];
}
public sealed class AssignmentEntity
{
    public int Count { get; set; } = 1;
    public string ReceiptId { get; set; } = "demo";
    public string ItemId { get; set; } = "";
    public string ParticipantId { get; set; } = "";
}

// Снимок личного итога сохраняется вместе с подтверждением чека.
public sealed class ReceiptMessage
{
    public long Id { get; set; }
    public required string ReceiptId { get; set; }
    public long ChatId { get; set; }
    public required string Text { get; set; }
    public string? OwnerLink { get; set; }
    public DateTimeOffset NextAttempt { get; set; }
}
