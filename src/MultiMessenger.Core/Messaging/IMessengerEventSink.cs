namespace MultiMessenger.Core.Messaging;

/// <summary>
/// Приёмник событий платформы. Коннектор получает его при создании и вызывает
/// по мере поступления апдейтов.
/// <para>
/// В плане это описано как события C# (<c>OnIncomingMessage</c> и прочие),
/// но обработка каждого события — это запись в базу, то есть асинхронная операция.
/// Обычные события такого не умеют: обработчик пришлось бы делать <c>async void</c>,
/// теряя и порядок обработки, и исключения. Интерфейс решает обе задачи
/// и вдобавок подменяется в тестах одной строкой.
/// </para>
/// </summary>
public interface IMessengerEventSink
{
    Task OnMessageReceivedAsync(IncomingMessage message, CancellationToken cancellationToken = default);

    Task OnMessageEditedAsync(MessageEdited edit, CancellationToken cancellationToken = default);

    Task OnMessageDeletedAsync(MessageDeleted deletion, CancellationToken cancellationToken = default);

    Task OnReadStatusChangedAsync(ReadStatusChanged change, CancellationToken cancellationToken = default);

    Task OnConnectionStateChangedAsync(ConnectionStateChanged change, CancellationToken cancellationToken = default);
}
