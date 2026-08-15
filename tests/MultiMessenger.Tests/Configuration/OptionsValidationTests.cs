using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MultiMessenger.Infrastructure.Configuration;

namespace MultiMessenger.Tests.Configuration;

/// <summary>
/// Проверяет, что неполная конфигурация ловится валидацией, а не превращается
/// в невнятную ошибку при первом обращении к MinIO или к файлу сессии.
/// </summary>
public class OptionsValidationTests
{
    private static readonly Dictionary<string, string?> ValidSettings = new()
    {
        ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=mm;Username=mm;Password=secret",
        ["Minio:Endpoint"] = "localhost:9000",
        ["Minio:AccessKey"] = "access",
        ["Minio:SecretKey"] = "secret",
        ["Minio:BucketName"] = "multimessenger-media",
        ["Minio:UseSsl"] = "false",
        ["Storage:SessionsBasePath"] = "./sessions",
    };

    [Fact]
    public void ValidConfigurationResolvesOptions()
    {
        var provider = BuildProvider(ValidSettings);

        var minio = provider.GetRequiredService<IOptions<MinioOptions>>().Value;
        minio.Endpoint.Should().Be("localhost:9000");
        minio.BucketName.Should().Be("multimessenger-media");
        minio.UseSsl.Should().BeFalse();

        provider.GetRequiredService<IOptions<StorageOptions>>().Value
            .SessionsBasePath.Should().Be("./sessions");
    }

    [Theory]
    [InlineData("Minio:Endpoint")]
    [InlineData("Minio:AccessKey")]
    [InlineData("Minio:SecretKey")]
    [InlineData("Minio:BucketName")]
    public void MissingMinioSettingFailsValidation(string missingKey)
    {
        var settings = new Dictionary<string, string?>(ValidSettings) { [missingKey] = string.Empty };
        var provider = BuildProvider(settings);

        var resolve = () => provider.GetRequiredService<IOptions<MinioOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainMatch($"*{missingKey.Split(':')[1]}*");
    }

    [Fact]
    public void MissingSessionsPathFailsValidation()
    {
        var settings = new Dictionary<string, string?>(ValidSettings) { ["Storage:SessionsBasePath"] = string.Empty };
        var provider = BuildProvider(settings);

        var resolve = () => provider.GetRequiredService<IOptions<StorageOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void MissingConnectionStringFailsAtRegistration()
    {
        var settings = new Dictionary<string, string?>(ValidSettings) { ["ConnectionStrings:Postgres"] = string.Empty };

        var register = () => BuildProvider(settings);

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:Postgres*");
    }

    /// <summary>
    /// Telegram-ключей на этапе 1 ещё нет, и приложение обязано стартовать без них:
    /// валидация должна срабатывать только при реальном обращении к настройкам.
    /// </summary>
    [Fact]
    public void MissingTelegramCredentialsDoNotBreakStartup()
    {
        var register = () => BuildProvider(ValidSettings);

        register.Should().NotThrow();
    }

    [Fact]
    public void MissingTelegramCredentialsFailWhenConnectorAsksForThem()
    {
        var provider = BuildProvider(ValidSettings);

        var resolve = () => provider.GetRequiredService<IOptions<TelegramOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>();
    }

    /// <summary>
    /// На сервере в Германии прокси не нужен — конфигурация без MTProxyUrl обязана быть валидной.
    /// </summary>
    [Fact]
    public void TelegramWithoutProxyIsValid()
    {
        var provider = BuildProvider(WithTelegramCredentials(new Dictionary<string, string?>
        {
            ["Telegram:UseProxy"] = "false",
        }));

        var telegram = provider.GetRequiredService<IOptions<TelegramOptions>>().Value;

        telegram.UseProxy.Should().BeFalse();
        telegram.MTProxyUrl.Should().BeNullOrEmpty();
    }

    [Fact]
    public void TelegramWithProxyRequiresProxyUrl()
    {
        var provider = BuildProvider(WithTelegramCredentials(new Dictionary<string, string?>
        {
            ["Telegram:UseProxy"] = "true",
        }));

        var resolve = () => provider.GetRequiredService<IOptions<TelegramOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainMatch("*MTProxyUrl*");
    }

    [Fact]
    public void TelegramWithProxyAndUrlIsValid()
    {
        var provider = BuildProvider(WithTelegramCredentials(new Dictionary<string, string?>
        {
            ["Telegram:UseProxy"] = "true",
            ["Telegram:MTProxyUrl"] = "https://t.me/proxy?server=example.com&port=443&secret=deadbeef",
        }));

        provider.GetRequiredService<IOptions<TelegramOptions>>().Value
            .UseProxy.Should().BeTrue();
    }

    private static Dictionary<string, string?> WithTelegramCredentials(Dictionary<string, string?> telegramSettings)
    {
        var settings = new Dictionary<string, string?>(ValidSettings)
        {
            ["Telegram:ApiId"] = "123456",
            ["Telegram:ApiHash"] = "0123456789abcdef0123456789abcdef",
        };

        foreach (var (key, value) in telegramSettings)
        {
            settings[key] = value;
        }

        return settings;
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new ServiceCollection()
            .AddMultiMessengerOptions(configuration)
            .BuildServiceProvider();
    }
}
