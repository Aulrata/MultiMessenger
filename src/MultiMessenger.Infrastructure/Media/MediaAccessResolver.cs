using Microsoft.EntityFrameworkCore;
using MultiMessenger.Infrastructure.Persistence;

namespace MultiMessenger.Infrastructure.Media;

/// <summary>
/// Разрешение доступа к вложению: есть ли оно и вправе ли сотрудник его получить.
/// <para>
/// Вынесено из эндпоинта отдельным классом, потому что это правило доступа
/// к персональным данным клиентов, а не деталь HTTP. Здесь оно покрывается
/// тестами против настоящей базы, вместе со всей цепочкой связей
/// вложение → сообщение → диалог → канал → владелец.
/// </para>
/// </summary>
public class MediaAccessResolver(AppDbContext dbContext)
{
    /// <summary>
    /// Возвращает описание вложения или <c>null</c>, если его нет либо доступа нет.
    /// <para>
    /// Отсутствие и запрет намеренно неразличимы: эндпоинт отвечает на оба случая
    /// одинаково, иначе перебором идентификаторов можно выяснить, какие вложения
    /// существуют в системе.
    /// </para>
    /// </summary>
    public async Task<MediaAccess?> ResolveAsync(
        Guid attachmentId,
        Guid managerId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var attachment = await dbContext.MediaAttachments
            .Where(candidate => candidate.Id == attachmentId)
            .Select(candidate => new
            {
                candidate.StorageKey,
                candidate.MimeType,
                candidate.FileName,
                OwnerManagerId = candidate.Message!.Dialog!.MessengerAccount!.ManagerId,
                candidate.Message.DialogId,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (attachment is null)
        {
            return null;
        }

        var isOwner = attachment.OwnerManagerId == managerId;

        if (!isOwner && !isAdmin)
        {
            return null;
        }

        return new MediaAccess(
            attachment.StorageKey,
            attachment.MimeType,
            attachment.FileName,
            attachment.OwnerManagerId,
            attachment.DialogId,
            isOwner);
    }
}

/// <summary>
/// Вложение, к которому доступ разрешён, вместе с данными для его отдачи.
/// </summary>
/// <param name="IsOwner">
/// Ложь означает, что вложение открывает администратор из чужой переписки, —
/// такое обращение попадает в журнал аудита.
/// </param>
public sealed record MediaAccess(
    string StorageKey,
    string? MimeType,
    string? FileName,
    Guid OwnerManagerId,
    Guid DialogId,
    bool IsOwner);
