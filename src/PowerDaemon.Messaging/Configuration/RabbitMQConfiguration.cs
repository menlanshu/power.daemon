namespace PowerDaemon.Messaging.Configuration;

public class RabbitMQConfiguration
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public bool EnableSsl { get; set; } = false;
    public int RequestedHeartbeat { get; set; } = 60;
    public int NetworkRecoveryInterval { get; set; } = 10;
    public bool AutomaticRecoveryEnabled { get; set; } = true;
    public string ExchangeName { get; set; } = "powerdaemon";
    public string DeploymentQueue { get; set; } = "powerdaemon.deployments";
    public string CommandQueue { get; set; } = "powerdaemon.commands";
    public string StatusQueue { get; set; } = "powerdaemon.status";
    public string DeadLetterExchange { get; set; } = "powerdaemon.dlx";
    public int MessageTtlSeconds { get; set; } = 3600; // 1 hour
    public int MaxRetryAttempts { get; set; } = 3;
}