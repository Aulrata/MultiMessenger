using FluentAssertions;
using MultiMessenger.Core.Enums;
using MultiMessenger.Infrastructure.Messengers.Telegram;
using TL;
using TelegramMessage = TL.Message;

namespace MultiMessenger.Tests.Messengers;

/// <summary>
/// Перевод MTProto в доменную модель — самая хрупкая часть коннектора.
/// Живой Telegram здесь не участвует: обращения к нему медленны, нестабильны
/// и упираются в лимиты, а проверять надо чистое преобразование.
/// </summary>
public class TelegramMessageNormalizerTests
{
    private static readonly Guid AccountId = Guid.CreateVersion7();
    private static readonly DateTime SentAt = new(2026, 8, 16, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void IncomingTextMessageIsNormalized()
    {
        var message = NewMessage(text: "Здравствуйте, интересует тур");

        var normalized = TelegramMessageNormalizer.ToIncomingMessage(message, AccountId);

        normalized.Should().NotBeNull();
        normalized!.MessengerAccountId.Should().Be(AccountId);
        normalized.PlatformUserId.Should().Be("777");
        normalized.PlatformMessageId.Should().Be("42");
        normalized.Direction.Should().Be(MessageDirection.Incoming);
        normalized.Text.Should().Be("Здравствуйте, интересует тур");
        normalized.OccurredAt.Should().Be(SentAt);
        normalized.Attachments.Should().BeEmpty();
    }

    /// <summary>
    /// Сообщение, отправленное менеджером с телефона, приходит тем же апдейтом,
    /// но с флагом out_. Без этого различения в истории появятся дыры (раздел 5.6 ТЗ).
    /// </summary>
    [Fact]
    public void OutgoingFlagYieldsOutgoingDirection()
    {
        var message = NewMessage(text: "Добрый день!", outgoing: true);

        TelegramMessageNormalizer.ToIncomingMessage(message, AccountId)!
            .Direction.Should().Be(MessageDirection.Outgoing);
    }

    [Fact]
    public void EmptyTextBecomesNull()
    {
        TelegramMessageNormalizer.ToIncomingMessage(NewMessage(text: ""), AccountId)!
            .Text.Should().BeNull("пустая строка и отсутствие текста — одно и то же");
    }

    /// <summary>Групповые чаты сервису не нужны: в них нет клиента, с которым ведётся работа.</summary>
    [Fact]
    public void GroupChatMessageIsSkipped()
    {
        var message = NewMessage(text: "привет всем");
        message.peer_id = new PeerChat { chat_id = 555 };

        TelegramMessageNormalizer.ToIncomingMessage(message, AccountId).Should().BeNull();
    }

    [Fact]
    public void VoiceMessageKeepsItsDuration()
    {
        var message = NewMessage(text: "");
        message.media = new MessageMediaDocument
        {
            document = new Document
            {
                mime_type = "audio/ogg",
                size = 24_576,
                attributes =
                [
                    new DocumentAttributeAudio
                    {
                        duration = 12,
                        flags = DocumentAttributeAudio.Flags.voice,
                    },
                ],
            },
        };

        var attachment = TelegramMessageNormalizer.ToIncomingMessage(message, AccountId)!
            .Attachments.Should().ContainSingle().Subject;

        attachment.Type.Should().Be(MediaType.Voice);
        attachment.DurationSeconds.Should().Be(12);
        attachment.SizeBytes.Should().Be(24_576);
        attachment.MimeType.Should().Be("audio/ogg");
    }

    /// <summary>
    /// Аудиофайл и голосовое различаются одним флагом, но в интерфейсе это разные
    /// вещи: у голосового рисуется звуковая дорожка.
    /// </summary>
    [Fact]
    public void AudioWithoutVoiceFlagIsADocument()
    {
        var message = NewMessage(text: "");
        message.media = new MessageMediaDocument
        {
            document = new Document
            {
                mime_type = "audio/mpeg",
                attributes =
                [
                    new DocumentAttributeAudio { duration = 180 },
                    new DocumentAttributeFilename { file_name = "песня.mp3" },
                ],
            },
        };

        var attachment = TelegramMessageNormalizer.ToIncomingMessage(message, AccountId)!.Attachments.Single();

        attachment.Type.Should().Be(MediaType.Document);
        attachment.FileName.Should().Be("песня.mp3");
        attachment.DurationSeconds.Should().BeNull();
    }

    [Fact]
    public void VideoDurationIsRoundedToSeconds()
    {
        var message = NewMessage(text: "");
        message.media = new MessageMediaDocument
        {
            document = new Document
            {
                mime_type = "video/mp4",
                attributes = [new DocumentAttributeVideo { duration = 8.6 }],
            },
        };

        var attachment = TelegramMessageNormalizer.ToIncomingMessage(message, AccountId)!.Attachments.Single();

        attachment.Type.Should().Be(MediaType.Video);
        attachment.DurationSeconds.Should().Be(9);
    }

    [Fact]
    public void DocumentKeepsOriginalFileName()
    {
        var message = NewMessage(text: "договор во вложении");
        message.media = new MessageMediaDocument
        {
            document = new Document
            {
                mime_type = "application/pdf",
                size = 102_400,
                attributes = [new DocumentAttributeFilename { file_name = "договор.pdf" }],
            },
        };

        var normalized = TelegramMessageNormalizer.ToIncomingMessage(message, AccountId)!;
        var attachment = normalized.Attachments.Single();

        attachment.Type.Should().Be(MediaType.Document);
        attachment.FileName.Should().Be("договор.pdf");
        normalized.Text.Should().Be("договор во вложении", "подпись к файлу — это текст сообщения");
    }

    [Fact]
    public void PhotoIsRecognized()
    {
        var message = NewMessage(text: "");
        message.media = new MessageMediaPhoto
        {
            photo = new Photo { sizes = [new PhotoSize { size = 51_200 }] },
        };

        var attachment = TelegramMessageNormalizer.ToIncomingMessage(message, AccountId)!.Attachments.Single();

        attachment.Type.Should().Be(MediaType.Photo);
        attachment.SizeBytes.Should().Be(51_200);
    }

    /// <summary>Опросы, геометки и прочая экзотика — не вложения, но и не повод терять текст.</summary>
    [Fact]
    public void UnsupportedMediaLeavesMessageWithoutAttachments()
    {
        var message = NewMessage(text: "смотри где я");
        message.media = new MessageMediaGeo();

        var normalized = TelegramMessageNormalizer.ToIncomingMessage(message, AccountId)!;

        normalized.Attachments.Should().BeEmpty();
        normalized.Text.Should().Be("смотри где я");
    }

    [Fact]
    public void FileReferenceRoundTrips()
    {
        var reference = TelegramMessageNormalizer.BuildFileReference(777, 42, 0);

        TelegramMessageNormalizer.TryParseFileReference(reference, out var peer, out var messageId, out var index)
            .Should().BeTrue();

        peer.Should().Be(777);
        messageId.Should().Be(42);
        index.Should().Be(0);
    }

    [Theory]
    [InlineData("мусор")]
    [InlineData("777:42")]
    [InlineData("777:сорок два:0")]
    public void MalformedFileReferenceIsRejected(string reference)
    {
        TelegramMessageNormalizer.TryParseFileReference(reference, out _, out _, out _).Should().BeFalse();
    }

    private static TelegramMessage NewMessage(string text, bool outgoing = false) => new()
    {
        id = 42,
        peer_id = new PeerUser { user_id = 777 },
        date = SentAt,
        message = text,
        flags = outgoing ? TelegramMessage.Flags.out_ : default,
    };
}
