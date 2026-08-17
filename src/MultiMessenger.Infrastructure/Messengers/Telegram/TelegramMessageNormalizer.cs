using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using TL;
using TelegramMessage = TL.Message;

namespace MultiMessenger.Infrastructure.Messengers.Telegram;

/// <summary>
/// Перевод объектов MTProto в доменные модели.
/// <para>
/// Вынесено отдельным классом без состояния и без обращений к сети: это самая хрупкая
/// часть коннектора, и она должна проверяться тестами, а не живой перепиской.
/// За пределы инфраструктуры типы <c>TL.*</c> не выходят — на этом держится
/// возможность добавить WhatsApp и MAX, не трогая остальную систему.
/// </para>
/// </summary>
public static class TelegramMessageNormalizer
{
    /// <summary>
    /// Приводит сообщение Telegram к доменной модели.
    /// Возвращает <c>null</c> для того, что системе не нужно: групповых чатов,
    /// служебных сообщений и сообщений без распознаваемого собеседника.
    /// </summary>
    public static IncomingMessage? ToIncomingMessage(TelegramMessage message, Guid messengerAccountId)
    {
        // Сервис ведёт переписку один на один. Группы и каналы пропускаем:
        // в них нет клиента, с которым работает менеджер.
        if (message.peer_id is not PeerUser peer)
        {
            return null;
        }

        var isOutgoing = message.flags.HasFlag(TelegramMessage.Flags.out_);

        return new IncomingMessage
        {
            MessengerAccountId = messengerAccountId,
            PlatformUserId = peer.user_id.ToString(),
            PlatformMessageId = message.id.ToString(),
            // Исходящие приходят сюда, когда менеджер пишет клиенту с телефона.
            // Без них в истории появились бы дыры (раздел 5.6 ТЗ).
            Direction = isOutgoing ? MessageDirection.Outgoing : MessageDirection.Incoming,
            Text = string.IsNullOrEmpty(message.message) ? null : message.message,
            OccurredAt = message.date,
            Attachments = ToAttachments(message),
        };
    }

    /// <summary>
    /// Ссылка на вложение, по которой его потом скачивают. Собирается из собеседника,
    /// номера сообщения и порядкового номера вложения: этих трёх чисел достаточно,
    /// чтобы найти файл заново даже после перезапуска приложения.
    /// </summary>
    public static string BuildFileReference(long peerUserId, int messageId, int attachmentIndex) =>
        $"{peerUserId}:{messageId}:{attachmentIndex}";

    public static bool TryParseFileReference(string reference, out long peerUserId, out int messageId, out int index)
    {
        peerUserId = 0;
        messageId = 0;
        index = 0;

        var parts = reference.Split(':');

        return parts.Length == 3
               && long.TryParse(parts[0], out peerUserId)
               && int.TryParse(parts[1], out messageId)
               && int.TryParse(parts[2], out index);
    }

    private static IReadOnlyList<IncomingAttachment> ToAttachments(TelegramMessage message)
    {
        if (message.peer_id is not PeerUser peer || message.media is null)
        {
            return [];
        }

        var attachment = message.media switch
        {
            MessageMediaPhoto { photo: Photo photo } => new IncomingAttachment
            {
                Type = MediaType.Photo,
                PlatformFileReference = BuildFileReference(peer.user_id, message.id, 0),
                MimeType = "image/jpeg",
                SizeBytes = LargestPhotoSize(photo),
            },
            MessageMediaDocument { document: Document document } => FromDocument(document, peer.user_id, message.id),
            _ => null,
        };

        return attachment is null ? [] : [attachment];
    }

    private static IncomingAttachment FromDocument(Document document, long peerUserId, int messageId)
    {
        var audio = document.attributes?.OfType<DocumentAttributeAudio>().FirstOrDefault();
        var video = document.attributes?.OfType<DocumentAttributeVideo>().FirstOrDefault();
        var fileName = document.attributes?.OfType<DocumentAttributeFilename>().FirstOrDefault()?.file_name;

        // Голосовое отличается от обычного аудио одним флагом, а в интерфейсе
        // это разные вещи: у голосового рисуется дорожка.
        var isVoice = audio?.flags.HasFlag(DocumentAttributeAudio.Flags.voice) is true;

        var type = (isVoice, video) switch
        {
            (true, _) => MediaType.Voice,
            (_, not null) => MediaType.Video,
            _ => MediaType.Document,
        };

        var duration = type switch
        {
            MediaType.Voice => audio?.duration,
            MediaType.Video => video is null ? null : (int?)Math.Round(video.duration),
            _ => null,
        };

        return new IncomingAttachment
        {
            Type = type,
            PlatformFileReference = BuildFileReference(peerUserId, messageId, 0),
            FileName = fileName,
            MimeType = document.mime_type,
            SizeBytes = document.size,
            DurationSeconds = duration,
        };
    }

    private static long? LargestPhotoSize(Photo photo) =>
        photo.sizes?.OfType<PhotoSize>().Select(size => (long)size.size).DefaultIfEmpty().Max() is { } largest and > 0
            ? largest
            : null;
}
