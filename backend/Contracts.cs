using System.ComponentModel.DataAnnotations;

namespace GeneralExpenses.Api;

// DTO описывают JSON-контракт. Amount — целое число копеек, никогда не float/double.
public record Participant(string Id, string Name, string Initial, bool Joined = false, bool Finished = false);
// Amount — общая цена строки. В режиме порций Counts хранит количество у каждого участника.
public record ReceiptItem(string Id, string Name, string Detail, long Amount, int Quantity = 1,
    bool ByPortion = false, Dictionary<string, int>? Counts = null);
public record ReceiptTotals(Dictionary<string, long> Totals, long Unassigned, long Total); // 	Суммы участников, нераспределённый остаток и общий итог
///
public record ReceiptDto(Participant[] Participants, ReceiptItem[] Items,
    Dictionary<string, string[]> Assignments, ReceiptTotals Result, long Revision,
    string InstanceId, string Id = "demo", string Title = "Ужин с друзьями", DateTimeOffset CreatedAt = default,
    string? MyParticipantId = null, bool IsOwner = false, bool CanSelectForOthers = false)
{
    // Данные уже сохраняются в БД. Правило действует и для ранее завершённых чеков.
    public bool IsCompleted => Items.Length > 0 && Result.Unassigned == 0 && Participants.All(p => p.Finished);
}
/// <summary>
/// Общие поля запроса на изменение.
/// </summary>
public class ChangeRequest
{
    [Required] public string? InstanceId { get; init; }
    [Required, Range(0, long.MaxValue)] public long? Revision { get; init; }
}
/// <summary>
/// Запрос на добавление или редактирование блюда
/// </summary>
public sealed class ItemRequest : ChangeRequest
{
    [Range(1, 999)] public int? Quantity { get; init; }
    public bool? ByPortion { get; init; }
    [Required, StringLength(100)] public string? Name { get; init; }
    [Required, Range(1, 100_000_000)] public long? Amount { get; init; }
}
/// <summary>
/// 	Запрос на выбор блюда участником
/// </summary>
public sealed class SelectionRequest : ChangeRequest
{
    [Range(0, 999)] public int? Count { get; init; }
    public bool AdminMode { get; init; }
    [Required] public bool? Selected { get; init; }
}

public sealed class ReceiptException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}


// Краткие карточки списка не загружают все блюда и назначения участников.
public record ReceiptSummaryDto(string Id, string Title, DateTimeOffset CreatedAt, int ParticipantCount, int ItemCount,
    long Total, bool CanDelete, string InstanceId, long Revision);
public sealed class CreateReceiptRequest
{
    [Required] public Guid? RequestId { get; init; }
    [Required, StringLength(100)] public string? Title { get; init; }
    [Required, MinLength(1), MaxLength(12)] public string[]? Participants { get; init; }
}
