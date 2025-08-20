namespace PowerDaemon.Orchestrator.Models;

public enum ServiceCommand
{
    Start,
    Stop,
    Restart,
    Status,
    Deploy,
    Rollback,
    HealthCheck,
    Configure,
    Update
}