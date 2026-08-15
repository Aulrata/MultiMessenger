using Microsoft.EntityFrameworkCore;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Storage;
using MultiMessenger.Infrastructure.Identity;
using MultiMessenger.Infrastructure.Persistence;
using MultiMessenger.Web.Security;

namespace MultiMessenger.Web.Endpoints;

public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/media/{id:guid}", GetMediaAsync)
            .RequireAuthorization(AuthorizationPolicies.Workspace);

        return endpoints;
    }

    /// <summary>
    /// Единственный способ, которым интерфейс показывает вложения. Ссылками
    /// мессенджеров пользоваться нельзя — они истекают, а файл должен открываться
    /// и через год.
    /// </summary>
    private static async Task<IResult> GetMediaAsync(
        Guid id,
        HttpContext httpContext,
        AppDbContext dbContext,
        IFileStorage fileStorage,
        IAuditTrail auditTrail,
        CancellationToken cancellationToken)
    {
        var attachment = await dbContext.MediaAttachments
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new
            {
                candidate.StorageKey,
                candidate.MimeType,
                candidate.FileName,
                OwnerManagerId = candidate.Message!.Dialog!.MessengerAccount!.ManagerId,
                DialogId = candidate.Message.DialogId,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (attachment is null)
        {
            return Results.NotFound();
        }

        var currentManagerId = httpContext.User.GetManagerId();

        if (currentManagerId is null)
        {
            return Results.Unauthorized();
        }

        var isOwner = attachment.OwnerManagerId == currentManagerId;
        var isAdmin = httpContext.User.IsAdmin();

        if (!isOwner && !isAdmin)
        {
            // Именно 404, а не 403: иначе перебором идентификаторов можно узнать,
            // какие вложения вообще существуют в системе.
            return Results.NotFound();
        }

        var file = await fileStorage.GetAsync(attachment.StorageKey, cancellationToken);

        if (file is null)
        {
            return Results.NotFound();
        }

        // Обращение администратора к чужому вложению — событие повышенной
        // чувствительности, попадает в журнал. Своё вложение менеджер открывает
        // десятки раз за день, засорять этим аудит незачем.
        if (!isOwner)
        {
            await auditTrail.RecordAsync(
                new AuditEntry
                {
                    ManagerId = currentManagerId,
                    ImpersonatedManagerId = attachment.OwnerManagerId,
                    Action = AuditAction.MediaDownloaded,
                    Subject = attachment.FileName,
                    EntityType = "MediaAttachment",
                    EntityId = id,
                    DetailsJson = $$"""{"dialogId":"{{attachment.DialogId}}"}""",
                    IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                },
                cancellationToken);
        }

        return Results.Stream(
            file.Content,
            attachment.MimeType ?? file.ContentType,
            attachment.FileName);
    }
}
