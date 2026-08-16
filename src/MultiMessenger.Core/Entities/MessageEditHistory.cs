using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Entities;

/// <summary>
/// Журнал изменений сообщения: правки и удаления, из любого источника.
/// <para>
/// В ТЗ таблица называется историей правок и хранит только предыдущий текст.
/// Здесь она расширена до полного журнала изменений — удаления фиксируются
/// в ней же с <see cref="MessageChangeType.Deleted"/>, чтобы история одного
/// сообщения читалась одним запросом, а не склейкой двух таблиц.
/// </para>
/// </summary>
public class MessageEditHistory
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MessageId { get; set; }

    public Message? Message { get; set; }

    public MessageChangeType ChangeType { get; set; } = MessageChangeType.Edited;

    public MessageChangeOrigin Origin { get; set; }

    /// <summary>
    /// Текст до изменения. Для удаления — текст на момент удаления: именно ради него
    /// строка и заводится, иначе содержимое теряется безвозвратно.
    /// </summary>
    public string? PreviousText { get; set; }

    /// <summary>
    /// Менеджер, выполнивший действие через наш интерфейс. Null, когда изменение
    /// пришло от клиента или из внешнего клиента менеджера. При работе через
    /// мультиаккаунт здесь тот, кто фактически нажал кнопку, а не владелец канала.
    /// </summary>
    public Guid? ChangedByManagerId { get; set; }

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
