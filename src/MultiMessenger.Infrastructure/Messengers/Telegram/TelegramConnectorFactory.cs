using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Configuration;

namespace MultiMessenger.Infrastructure.Messengers.Telegram;

public sealed class TelegramConnectorFactory(
    IOptions<TelegramOptions> telegramOptions,
    IOptions<StorageOptions> storageOptions,
    ILoggerFactory loggerFactory) : IMessengerConnectorFactory
{
    public MessengerPlatform Platform => MessengerPlatform.Telegram;

    public IMessengerConnector Create(Guid messengerAccountId, IMessengerEventSink sink)
    {
        // Обращение к Value здесь и запускает проверку TelegramOptions: до этого
        // момента ключи не нужны, и приложение поднимается без них.
        var telegram = telegramOptions.Value;

        var settings = new TelegramConnectorSettings
        {
            ApiId = telegram.ApiId,
            ApiHash = telegram.ApiHash,
            SessionsBasePath = storageOptions.Value.SessionsBasePath,
            MTProxyUrl = telegram.UseProxy ? telegram.MTProxyUrl : null,
        };

        return new TelegramConnector(
            messengerAccountId,
            settings,
            sink,
            loggerFactory.CreateLogger<TelegramConnector>());
    }
}
