using PowerDaemon.Shared.DTOs;

namespace PowerDaemon.Agent.Services;

public interface IMetricsCollector
{
    Task<MetricBatchDto> CollectMetricsAsync(CancellationToken cancellationToken = default);
    Task StartCollectionAsync(CancellationToken cancellationToken = default);
    Task StopCollectionAsync();
}