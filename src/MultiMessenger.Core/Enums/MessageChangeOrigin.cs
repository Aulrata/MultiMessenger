namespace MultiMessenger.Core.Enums;

/// <summary>
/// Откуда пришло изменение сообщения. Различать источники обязательно: правка,
/// сделанная менеджером у нас, и правка, прилетевшая апдейтом с его телефона, —
/// разные события, хотя приводят к одному результату в тексте.
/// </summary>
public enum MessageChangeOrigin
{
    /// <summary>Клиент изменил или удалил своё сообщение.</summary>
    Client,

    /// <summary>Менеджер сделал это через наш интерфейс.</summary>
    ManagerViaService,

    /// <summary>
    /// Менеджер сделал это в официальном клиенте на телефоне — узнали из апдейта
    /// с флагом <c>out_ = true</c>.
    /// </summary>
    ManagerViaExternalClient,
}
