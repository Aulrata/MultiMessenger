using MultiMessenger.Core.Auditing;
using MultiMessenger.Infrastructure.Persistence;

namespace MultiMessenger.Infrastructure.Auditing;

public class EfAuditTrail(AppDbContext dbContext) : IAuditTrail
{
    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        dbContext.AuditEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
