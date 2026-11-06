using System.ComponentModel.DataAnnotations;

namespace GeneralExpenses.Api;

// DTO описывают JSON-контракт. Amount — целое число копеек, никогда не float/double.
public record Participant(string Id, string Name, string Initial); // 	Участник: идентификатор, имя, буква для аватара
public record ReceiptItem(string Id, string Name, string Detail, long Amount); // 	Позиция чека: название, описание и стоимость
public record ReceiptTotals(Dictionary<string, long> Totals, long Unassigned, long Total); // 	Суммы участников, нераспределённый остаток и общий итог
public record DeletedItem(ReceiptItem Item, int Index, string[] Owners); // 	Удалённая позиция, её место и участники — для отмены
///
public record ReceiptDto(Participant[] Participants, ReceiptItem[] Items,
    Dictionary<string, string[]> Assignments, ReceiptTotals Result, long Revision,
    string InstanceId, DeletedItem? Deleted); // 	Полное состояние чека, которое сервер возвращает интерфейсу
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
    [Required, StringLength(100)] public string? Name { get; init; }
    [Required, Range(1, 100_000_000)] public long? Amount { get; init; }
}
/// <summary>
/// 	Запрос на выбор блюда участником
/// </summary>
public sealed class SelectionRequest : ChangeRequest
{
    [Required] public bool? Selected { get; init; }
}

public sealed class ReceiptException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
