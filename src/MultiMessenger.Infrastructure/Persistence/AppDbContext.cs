using Microsoft.EntityFrameworkCore;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Entities;

namespace MultiMessenger.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Manager> Managers => Set<Manager>();

    public DbSet<MessengerAccount> MessengerAccounts => Set<MessengerAccount>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<ContactIdentity> ContactIdentities => Set<ContactIdentity>();

    public DbSet<Dialog> Dialogs => Set<Dialog>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<MessageEditHistory> MessageEditHistory => Set<MessageEditHistory>();

    public DbSet<MediaAttachment> MediaAttachments => Set<MediaAttachment>();

    public DbSet<AccountHealthLog> AccountHealthLogs => Set<AccountHealthLog>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <remarks>
    /// Важно при работе с этим контекстом: сущности проставляют себе <c>Id</c>
    /// в инициализаторе (Guid v7 ради упорядоченности индексов). Из-за заполненного
    /// ключа EF считает новую запись, добавленную в коллекцию уже отслеживаемого
    /// родителя, существующей и выдаёт UPDATE вместо INSERT. Поэтому дочерние
    /// сущности к загруженному родителю добавляются через <c>DbSet.Add</c>
    /// с явным указанием внешнего ключа, а не через <c>parent.Children.Add</c>.
    /// Для целиком нового графа это неактуально: <c>Add</c> помечает Added всё дерево.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Перечисления хранятся именами, а не числами: журнал аудита и таблицу сообщений
        // регулярно читают SQL-запросом, и "Failed" понятнее, чем 4. Плата — несколько
        // лишних байт на строку, что при нашем объёме несущественно.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);

        // Строки без явного ограничения — text, а не varchar(max-по-умолчанию).
        configurationBuilder.Properties<string>().AreUnicode();
    }
}
