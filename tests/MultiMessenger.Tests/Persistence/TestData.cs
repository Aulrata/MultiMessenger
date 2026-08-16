using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Storage;
using MultiMessenger.Infrastructure.Persistence;

namespace MultiMessenger.Tests.Persistence;

/// <summary>
/// Подготовка данных для интеграционных тестов. Номера телефонов генерируются
/// уникальными: контейнер с базой один на всю коллекцию, а на номер сотрудника
/// стоит уникальный индекс.
/// </summary>
public static class TestData
{
    public static string NewPhoneNumber() => $"+79{Random.Shared.NextInt64(100_000_000, 999_999_999)}";

    public static Manager NewManager(
        string passwordHash,
        ManagerRole role = ManagerRole.Manager,
        bool isActive = true,
        string? phoneNumber = null) => new()
    {
        FullName = "Тестовый Сотрудник",
        PhoneNumber = phoneNumber ?? NewPhoneNumber(),
        PasswordHash = passwordHash,
        Role = role,
        IsActive = isActive,
    };

    /// <summary>
    /// Создаёт полную цепочку сотрудник → канал → контакт → диалог → сообщение → вложение.
    /// Именно по ней эндпоинт медиа определяет владельца.
    /// </summary>
    public static async Task<MediaChain> CreateMediaChainAsync(AppDbContext dbContext, Manager owner)
    {
        var account = new MessengerAccount
        {
            ManagerId = owner.Id,
            Platform = MessengerPlatform.Telegram,
            PhoneNumber = owner.PhoneNumber,
            Status = MessengerAccountStatus.Active,
        };

        var contact = new Contact
        {
            DisplayName = "Тестовый клиент",
            PrimaryPlatform = MessengerPlatform.Telegram,
            Identities =
            [
                new ContactIdentity
                {
                    Platform = MessengerPlatform.Telegram,
                    PlatformUserId = $"tg-{Guid.CreateVersion7()}",
                },
            ],
        };

        var dialog = new Dialog
        {
            ContactId = contact.Id,
            MessengerAccountId = account.Id,
            Platform = MessengerPlatform.Telegram,
            LastMessageAt = DateTimeOffset.UtcNow,
        };

        var attachment = new MediaAttachment
        {
            Type = MediaType.Document,
            StorageKey = MediaStorageKey.Create(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "договор.pdf"),
            FileName = "договор.pdf",
            MimeType = "application/pdf",
            SizeBytes = 1024,
        };

        var message = new Message
        {
            DialogId = dialog.Id,
            Direction = MessageDirection.Incoming,
            SenderType = SenderType.Client,
            PlatformMessageId = Random.Shared.Next(1, int.MaxValue).ToString(),
            Text = "Вот договор",
            Status = MessageStatus.Delivered,
            CreatedAt = DateTimeOffset.UtcNow,
            Attachments = [attachment],
        };

        dbContext.MessengerAccounts.Add(account);
        dbContext.Contacts.Add(contact);
        dbContext.Dialogs.Add(dialog);
        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync();

        return new MediaChain(account, contact, dialog, message, attachment);
    }

    public sealed record MediaChain(
        MessengerAccount Account,
        Contact Contact,
        Dialog Dialog,
        Message Message,
        MediaAttachment Attachment);
}
