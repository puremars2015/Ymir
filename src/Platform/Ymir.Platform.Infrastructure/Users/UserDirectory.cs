using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Identity;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.Platform.Users;

namespace Ymir.Platform.Infrastructure.Users;

internal sealed class UserDirectory(PlatformDbContext db, TimeProvider timeProvider) : IUserDirectory
{
    public async Task<User> UpsertOnLoginAsync(ExternalIdentity identity, UserRole roleForNewUser, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var now = timeProvider.GetUtcNow();
        var user = await db.Users
            .SingleOrDefaultAsync(u => u.Issuer == identity.Issuer && u.Subject == identity.Subject, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            user = User.Create(identity, roleForNewUser, now);
            db.Users.Add(user);
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
            return await UpsertOnLoginAsync(identity, roleForNewUser, cancellationToken).ConfigureAwait(false);
        }

        return user;
    }

    public Task<User?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
}
