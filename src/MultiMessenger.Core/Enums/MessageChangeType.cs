namespace MultiMessenger.Core.Enums;

public enum MessageChangeType
{
    /// <summary>Текст изменён. Прошлая версия сохранена в записи истории.</summary>
    Edited,

    /// <summary>
    /// Сообщение удалено. Строка в <c>Messages</c> остаётся с флагом <c>IsDeleted</c>,
    /// текст на момент удаления сохраняется в истории — внутренний архив переписки
    /// не должен терять содержимое из-за действий на стороне платформы.
    /// </summary>
    Deleted,
}
