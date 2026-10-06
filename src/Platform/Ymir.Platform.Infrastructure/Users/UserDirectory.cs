using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Identity;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.Platform.Users;

namespace Ymir.Platform.Infrastructure.Users;

internal sealed class UserDirectory(PlatformDbContext db, TimeProvider timeProvider) : IUserDirectory
{
    public async Task<User> UpsertOnLoginAsync(ExternalIdentity identity, UserRole role, CancellationToken cancellationToken, bool roleManagedByIdentityProvider = false)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var now = timeProvider.GetUtcNow();
        var user = await db.Users
            .SingleOrDefaultAsync(u => u.Issuer == identity.Issuer && u.Subject == identity.Subject, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            user = User.Create(identity, role, now);
            db.Users.Add(user);
        }
        else if (roleManagedByIdentityProvider)
        {
            user.SyncRole(role, now);
        }

        user.RecordLogin(identity, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (db.Entry(user).State == EntityState.Added)
        {
            // 同一使用者同時第一次登入：唯一索引擋下第二筆，改讀已存在的資料。
            db.Entry(user).State = EntityState.Detached;
            return await UpsertOnLoginAsync(identity, role, cancellationToken, roleManagedByIdentityProvider).ConfigureAwait(false);
        }

        return user;
    }

    public Task<User?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public async Task<IReadOnlyList<User>> ListAsync(string? search, int take, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u => u.DisplayName.Contains(term) || (u.AccountName != null && u.AccountName.Contains(term)) || (u.Email != null && u.Email.Contains(term)));
        }

        return await query
            .OrderByDescending(u => u.LastLoginAt)
            .ThenBy(u => u.DisplayName)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<User>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return [];
        }

        var ids = userIds.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> HasActiveLocalAdminAsync(CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(u => u.Issuer == LocalAccounts.Issuer && u.Role == UserRole.Admin && u.Status == UserStatus.Active, cancellationToken);

    public async Task<UserStatistics> GetStatisticsAsync(DateTimeOffset activeSince, CancellationToken cancellationToken)
    {
        var counts = await db.Users.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Disabled = g.Count(u => u.Status == UserStatus.Disabled),
                Admins = g.Count(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active),
                Recent = g.Count(u => u.LastLoginAt >= activeSince),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return counts is null
            ? new UserStatistics(0, 0, 0, 0, 0)
            : new UserStatistics(counts.Total, counts.Total - counts.Disabled, counts.Disabled, counts.Admins, counts.Recent);
    }

    public async Task<User?> SetStatusAsync(Guid userId, UserStatus status, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (status == UserStatus.Disabled)
        {
            user.Disable(now);
        }
        else
        {
            user.Enable(now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return user;
    }
}
