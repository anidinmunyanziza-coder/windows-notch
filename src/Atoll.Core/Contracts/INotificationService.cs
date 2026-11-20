using System.Threading.Tasks;

namespace Atoll.Core.Contracts;

/// <summary>
/// Windows notifications (toasts). Translated from macOS user notifications
/// used by live activities / HUDs.
/// </summary>
public interface INotificationService : IDisposable
{
    Task ShowAsync(string title, string? body = null, string? tag = null, CancellationToken ct = default);
    Task HideAsync(string tag, CancellationToken ct = default);
}
