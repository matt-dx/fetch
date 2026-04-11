using Fetch.Core.Configuration;

namespace Fetch.Core.Orchestration;

/// <summary>
/// Creates <see cref="DownloadOrchestrator"/> instances for individual downloads.
/// Enables per-URI service construction while staying within the DI container,
/// so cross-cutting concerns (logging, telemetry, test doubles) are respected.
/// </summary>
public interface IOrchestratorFactory
{
    DownloadOrchestrator Create(DownloadOptions options);
}
