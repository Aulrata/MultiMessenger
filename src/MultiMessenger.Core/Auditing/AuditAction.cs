namespace MultiMessenger.Core.Auditing;

/// <summary>
/// Действия, попадающие в журнал аудита. Список пополняется по мере появления
/// функциональности. В БД сохраняется имя элемента, а не число, — журнал должен
/// читаться обычным SQL-запросом без сверки со справочником.
/// </summary>
public enum AuditAction
{
    // Доступ к системе (этап 1.5)
    ManagerSignedIn = 1,
    ManagerSignInFailed = 2,
    ManagerSignedOut = 3,

    // Администрирование (этап 1.5)
    ManagerCreated = 10,
    ManagerDeactivated = 11,
    ManagerPasswordChanged = 12,

    // Подключение каналов (этапы 2.4, 3.1, 3.2)
    MessengerAccountConnected = 20,
    MessengerAccountDisconnected = 21,
    MessengerAccountReauthRequired = 22,

    // Работа с перепиской (этапы 2.6, 2.7)
    MessageSent = 30,
    DialogOpened = 31,
    MediaDownloaded = 32,
    MessageEdited = 33,
    MessageDeleted = 34,

    // Работа с клиентами (этапы 2.7, 3.3)
    ContactUpdated = 40,
    ContactsMerged = 41,

    /// <summary>
    /// Просмотр чужой переписки — администратором из админки или сотрудником,
    /// работающим через мультиаккаунт. Событие повышенной чувствительности:
    /// именно ради него журнал и нужен.
    /// </summary>
    ForeignDialogOpened = 50,

    /// <summary>Вход в аккаунт другого сотрудника.</summary>
    ImpersonationStarted = 51,

    /// <summary>Возврат в собственный аккаунт.</summary>
    ImpersonationEnded = 52,
}
