using PowerDaemon.Orchestrator.Strategies;
using PowerDaemon.Messaging.Messages;

namespace PowerDaemon.Orchestrator.Services;

public interface IStrategyFactory
{
    IDeploymentStrategy GetStrategy(DeploymentStrategy strategyType);
    IEnumerable<IDeploymentStrategy> GetAllStrategies();
    bool IsStrategySupported(DeploymentStrategy strategyType);
}

public class StrategyFactory : IStrategyFactory
{
    private readonly Dictionary<DeploymentStrategy, IDeploymentStrategy> _strategies;

    public StrategyFactory(IEnumerable<IDeploymentStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(s => s.StrategyType, s => s);
    }

    public IDeploymentStrategy GetStrategy(DeploymentStrategy strategyType)
    {
        if (_strategies.TryGetValue(strategyType, out var strategy))
        {
            return strategy;
        }

        throw new NotSupportedException($"Deployment strategy {strategyType} is not supported");
    }

    public IEnumerable<IDeploymentStrategy> GetAllStrategies()
    {
        return _strategies.Values;
    }

    public bool IsStrategySupported(DeploymentStrategy strategyType)
    {
        return _strategies.ContainsKey(strategyType);
    }
}