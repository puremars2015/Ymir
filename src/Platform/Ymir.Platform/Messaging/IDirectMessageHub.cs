namespace Ymir.Platform.Messaging;

/// <summary>
/// 每位使用者的即時通知（SSE，ADR-0023）。只在單一 API 程序內傳遞：錯過的通知不補送，
/// 前端重新連線時重新讀取聯絡人名單與目前對話即可補齊（資料都在資料庫）。
/// </summary>
public interface IDirectMessageHub
{
    IDirectMessageSubscription Subscribe(Guid userId);

    void Publish(Guid userId, DirectMessageNotification notification);
}

public interface IDirectMessageSubscription : IDisposable
{
    IAsyncEnumerable<DirectMessageNotification> ReadAllAsync(CancellationToken cancellationToken);
}

public abstract record DirectMessageNotification;

/// <summary>新訊息；寄件者與收件者都會收到（寄件者的其他分頁也要同步）。</summary>
public sealed record DirectMessageSentNotification(DirectMessageItem Message) : DirectMessageNotification;

/// <summary>訊息已讀；讀取者（其他分頁更新未讀數）與寄件者（已讀標示）都會收到。</summary>
public sealed record DirectMessagesReadNotification(DirectMessagesRead Read) : DirectMessageNotification;
