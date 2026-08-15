namespace MultiMessenger.Core.Enums;

public enum MessengerAccountStatus
{
    /// <summary>Логин начат, но не завершён: ждём код или пароль 2FA.</summary>
    PendingAuth,

    /// <summary>Подключён и работает.</summary>
    Active,

    /// <summary>Связь потеряна, идут попытки переподключения по сохранённой сессии.</summary>
    Disconnected,

    /// <summary>Сессия невалидна — реальный логаут, а не обрыв. Нужен повторный вход менеджера.</summary>
    RequiresReauth,
}
