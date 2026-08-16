using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Storage;
using MultiMessenger.Infrastructure.Identity;
using MultiMessenger.Infrastructure.Media;
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
        MediaAccessResolver accessResolver,
        IFileStorage fileStorage,
        IAuditTrail auditTrail,
        CancellationToken cancellationToken)
    {
        var managerId = httpContext.User.GetManagerId();

        if (managerId is null)
        {
            return Results.Unauthorized();
        }

        var access = await accessResolver.ResolveAsync(
            id, managerId.Value, httpContext.User.IsAdmin(), cancellationToken);

        // Не найдено и не положено выглядят одинаково — см. комментарий в MediaAccessResolver.
        if (access is null)
        {
            return Results.NotFound();
        }

        var file = await fileStorage.GetAsync(access.StorageKey, cancellationToken);

        if (file is null)
        {
            return Results.NotFound();
        }

        // Обращение администратора к чужому вложению — событие повышенной
        // чувствительности, попадает в журнал. Своё вложение менеджер открывает
        // десятки раз за день, засорять этим аудит незачем.
        if (!access.IsOwner)
        {
            await auditTrail.RecordAsync(
                new AuditEntry
                {
                    ManagerId = managerId,
                    ImpersonatedManagerId = access.OwnerManagerId,
                    Action = AuditAction.MediaDownloaded,
                    Subject = access.FileName,
                    EntityType = "MediaAttachment",
                    EntityId = id,
                    DetailsJson = $$"""{"dialogId":"{{access.DialogId}}"}""",
                    IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                },
                cancellationToken);
        }

        return Results.Stream(file.Content, access.MimeType ?? file.ContentType, access.FileName);
    }
}
