using Ymir.Api.Auth;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.Settings;
using Ymir.VibeMaker.Contracts.Models;
using Ymir.VibeMaker.Contracts.Settings;

namespace Ymir.Api.Endpoints;

/// <summary>可選用的模型與目前使用者的個人設定。都只作用在自己身上，不接受任何外部 id（SA §12）。</summary>
internal static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/models", async (ModelAccessService policy, ICurrentUser user, CancellationToken ct) =>
            {
                var state = await policy.GetForUserAsync(user.UserId, ct);
                // 使用者只看模型名稱，不回傳 provider；同名模型才加上 provider 區分（ADR-0020 補充）。
                var names = ModelNames.ForUsers(state.Models);
                return state.Models.Select((m, i) => new ModelResponse(m.Id, names[i], m.Id == state.DefaultModelId, m.SupportsImages, m.EffectiveThinking is not null,
                    m.EffectiveThinking is { } thinking ? new ThinkingCapabilityResponse(thinking.Parameter, thinking.Levels, thinking.DefaultLevel, thinking.Required) : null, m.AllowKnowledgeBase)).ToList();
            })
            .WithName("ListModels")
            .WithTags("Models");

        var settings = endpoints.MapGroup("/api/me/settings").WithTags("Settings").RequireAntiforgeryHeader();

        settings.MapGet("/", (UserSettingsService service, CancellationToken ct) => service.GetAsync(ct))
            .WithName("GetMySettings");

        settings.MapPut("/", (UpdateUserSettingsRequest request, UserSettingsService service, CancellationToken ct) => service.UpdateAsync(request, ct))
            .WithName("UpdateMySettings");

        return endpoints;
    }
}
