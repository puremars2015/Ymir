using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Settings;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Settings;

/// <summary>目前使用者的個人設定；只讀寫自己的資料（SA §12），不接受任何外部 user id。</summary>
public sealed class UserSettingsService(IVibeMakerDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
{
    public async Task<UserSettingsResponse> GetAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var settings = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken).ConfigureAwait(false);
        return new UserSettingsResponse(settings?.SystemPrompt);
    }

    public async Task<UserSettingsResponse> UpdateAsync(UpdateUserSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.UserId;
        var now = timeProvider.GetUtcNow();
        var settings = await db.UserSettings.SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (settings is null)
        {
            settings = UserSettings.Create(userId, now);
            db.UserSettings.Add(settings);
        }

        settings.SetSystemPrompt(request.SystemPrompt, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new UserSettingsResponse(settings.SystemPrompt);
    }
}
