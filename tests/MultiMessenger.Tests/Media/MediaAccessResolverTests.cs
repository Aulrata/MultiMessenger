using FluentAssertions;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Infrastructure.Media;
using MultiMessenger.Tests.Persistence;

namespace MultiMessenger.Tests.Media;

/// <summary>
/// Правило доступа к вложениям: менеджер видит только свои, администратор — любые,
/// посторонний не видит ничего и не может выяснить, что вложение вообще есть.
/// Переписка с клиентами — персональные данные, поэтому проверяется на настоящей
/// базе, со всей цепочкой связей до владельца канала.
/// </summary>
[Collection(PostgresCollection.Name)]
public class MediaAccessResolverTests(PostgresFixture postgres)
{
    [Fact]
    public async Task OwnerGetsAccessToOwnAttachment()
    {
        var (owner, chain) = await CreateChainAsync();

        var access = await ResolveAsync(chain.Attachment.Id, owner.Id, isAdmin: false);

        access.Should().NotBeNull();
        access!.IsOwner.Should().BeTrue();
        access.StorageKey.Should().Be(chain.Attachment.StorageKey);
        access.FileName.Should().Be("договор.pdf");
        access.MimeType.Should().Be("application/pdf");
        access.OwnerManagerId.Should().Be(owner.Id);
        access.DialogId.Should().Be(chain.Dialog.Id);
    }

    /// <summary>
    /// Администратор видит чужие вложения, но помечается как не-владелец —
    /// именно по этому признаку эндпоинт пишет запись в журнал аудита.
    /// </summary>
    [Fact]
    public async Task AdminGetsAccessToForeignAttachmentButIsNotOwner()
    {
        var (owner, chain) = await CreateChainAsync();
        var admin = await CreateManagerAsync(ManagerRole.Admin);

        var access = await ResolveAsync(chain.Attachment.Id, admin.Id, isAdmin: true);

        access.Should().NotBeNull();
        access!.IsOwner.Should().BeFalse();
        access.OwnerManagerId.Should().Be(owner.Id);
    }

    [Fact]
    public async Task StrangerGetsNothing()
    {
        var (_, chain) = await CreateChainAsync();
        var stranger = await CreateManagerAsync(ManagerRole.Manager);

        var access = await ResolveAsync(chain.Attachment.Id, stranger.Id, isAdmin: false);

        access.Should().BeNull();
    }

    /// <summary>
    /// Несуществующее вложение и чужое обязаны быть неразличимы: иначе перебором
    /// идентификаторов можно узнать, какие вложения есть в системе.
    /// </summary>
    [Fact]
    public async Task MissingAttachmentIsIndistinguishableFromForbiddenOne()
    {
        var (_, chain) = await CreateChainAsync();
        var stranger = await CreateManagerAsync(ManagerRole.Manager);

        var forbidden = await ResolveAsync(chain.Attachment.Id, stranger.Id, isAdmin: false);
        var missing = await ResolveAsync(Guid.CreateVersion7(), stranger.Id, isAdmin: false);

        forbidden.Should().BeNull();
        missing.Should().BeNull();
    }

    [Fact]
    public async Task AdminAlsoGetsNothingForMissingAttachment()
    {
        var admin = await CreateManagerAsync(ManagerRole.Admin);

        (await ResolveAsync(Guid.CreateVersion7(), admin.Id, isAdmin: true)).Should().BeNull();
    }

    /// <summary>
    /// Владелец определяется по владельцу канала, а не по тому, кто отправил сообщение.
    /// Своё вложение открывается и без прав администратора.
    /// </summary>
    [Fact]
    public async Task OwnershipComesFromTheChannelOwner()
    {
        var (owner, chain) = await CreateChainAsync();

        var asPlainManager = await ResolveAsync(chain.Attachment.Id, owner.Id, isAdmin: false);
        var asAdmin = await ResolveAsync(chain.Attachment.Id, owner.Id, isAdmin: true);

        asPlainManager!.IsOwner.Should().BeTrue();
        asAdmin!.IsOwner.Should().BeTrue("администратор в собственной переписке остаётся владельцем");
    }

    private async Task<(Manager Owner, TestData.MediaChain Chain)> CreateChainAsync()
    {
        var owner = await CreateManagerAsync(ManagerRole.Manager);

        await using var dbContext = postgres.CreateDbContext();
        var chain = await TestData.CreateMediaChainAsync(dbContext, owner);

        return (owner, chain);
    }

    private async Task<Manager> CreateManagerAsync(ManagerRole role)
    {
        var manager = TestData.NewManager("hash", role);

        await using var dbContext = postgres.CreateDbContext();
        dbContext.Managers.Add(manager);
        await dbContext.SaveChangesAsync();

        return manager;
    }

    private async Task<MediaAccess?> ResolveAsync(Guid attachmentId, Guid managerId, bool isAdmin)
    {
        await using var dbContext = postgres.CreateDbContext();

        return await new MediaAccessResolver(dbContext).ResolveAsync(attachmentId, managerId, isAdmin);
    }
}
