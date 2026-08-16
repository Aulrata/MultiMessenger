using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MultiMessenger.Core.Enums;
using MultiMessenger.Infrastructure.Identity;

namespace MultiMessenger.Web.Security;

public static class AuthenticationSetup
{
    public const string LoginPath = "/login";
    public const string LogoutPath = "/account/logout";
    public const string AccessDeniedPath = "/access-denied";

    public static IServiceCollection AddManagerAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = LoginPath;
                options.LogoutPath = LogoutPath;
                options.AccessDeniedPath = AccessDeniedPath;
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
                options.Cookie.Name = "MultiMessenger.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                // Деактивация сотрудника должна выкидывать его из системы, а не ждать
                // истечения куки. Проверяем на каждом запросе — при десяти сотрудниках
                // это один дешёвый запрос по первичному ключу.
                options.Events.OnValidatePrincipal = ValidateManagerIsStillActiveAsync;
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.Workspace, policy =>
                policy.RequireRole(nameof(ManagerRole.Manager)));

            options.AddPolicy(AuthorizationPolicies.Administration, policy =>
                policy.RequireRole(nameof(ManagerRole.Admin)));
        });

        return services;
    }

    private static async Task ValidateManagerIsStillActiveAsync(CookieValidatePrincipalContext context)
    {
        var managerId = context.Principal?.GetManagerId();

        if (managerId is null)
        {
            context.RejectPrincipal();
            return;
        }

        var directory = context.HttpContext.RequestServices.GetRequiredService<ManagerDirectory>();
        var manager = await directory.FindByIdAsync(managerId.Value, context.HttpContext.RequestAborted);

        if (manager is null || !manager.IsActive)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}

