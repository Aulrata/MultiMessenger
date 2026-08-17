using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Messaging;

/// <summary>
/// Единая точка общения с мессенджером. Один экземпляр обслуживает один
/// подключённый аккаунт.
/// <para>
/// Всё, что за пределами этого интерфейса — интерфейс, воркеры, база — работает
/// только с нормализованными моделями из этого пространства имён. Типы
/// WTelegramClient, Baileys и клиента MAX не должны покидать слой инфраструктуры:
/// от этого зависит, получится ли добавить WhatsApp и MAX на третьем этапе,
/// не переписывая всё остальное.
/// </para>
/// </summary>
public interface IMessengerConnector : IAsyncDisposable
{
    MessengerPlatform Platform { get; }

    /// <summary>Что эта платформа умеет — см. <see cref="PlatformCapabilities"/>.</summary>
    PlatformCapabilities Capabilities { get; }

    Guid MessengerAccountId { get; }

    // --- Подключение -----------------------------------------------------

    /// <summary>
    /// Начинает вход. Возвращает, что требуется дальше: у Telegram это код,
    /// у WhatsApp — QR-код, у MAX вход завершается сразу.
    /// </summary>
    Task<LoginStep> BeginLoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Передаёт ответ пользователя и возвращает следующий шаг. Вызывается столько раз,
    /// сколько нужно платформе: ожидание сканирования QR-кода — это повторные вызовы
    /// с <see cref="LoginAnswer.CheckStatus"/>.
    /// </summary>
    Task<LoginStep> ContinueLoginAsync(LoginAnswer answer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Поднимает соединение по ранее сохранённой сессии — при старте приложения
    /// и при восстановлении после обрыва. Для платформ без постоянного соединения
    /// (MAX) сводится к проверке токена.
    /// </summary>
    Task<ConnectionState> ConnectExistingAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    // --- Работа с сообщениями --------------------------------------------

    Task<DeliveryResult> SendMessageAsync(OutgoingMessage message, CancellationToken cancellationToken = default);

    Task<DeliveryResult> EditMessageAsync(EditMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет сообщение на стороне платформы. По принятому решению — у всех
    /// участников, а не только у нас. Запись в нашей базе при этом сохраняется
    /// с пометкой об удалении.
    /// </summary>
    Task<DeliveryResult> DeleteMessageAsync(DeleteMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Скачивает вложение по ссылке из <see cref="IncomingAttachment.PlatformFileReference"/>.
    /// Вызывающий код обязан освободить поток.
    /// </summary>
    Task<Stream> DownloadAttachmentAsync(string platformFileReference, CancellationToken cancellationToken = default);

    // --- История ---------------------------------------------------------

    /// <summary>
    /// Перечисляет накопленную переписку для первичной загрузки. Возвращает поток,
    /// а не список: у активного аккаунта Telegram это десятки тысяч сообщений,
    /// и держать их в памяти разом незачем.
    /// <para>
    /// Платформы без истории (<see cref="PlatformCapabilities.SupportsHistoryBackfill"/>
    /// равно <c>false</c>) возвращают пустую последовательность, а не бросают исключение:
    /// загрузчику истории не должно быть дела до того, с кем он работает.
    /// </para>
    /// </summary>
    IAsyncEnumerable<IncomingMessage> EnumerateHistoryAsync(CancellationToken cancellationToken = default);
}
