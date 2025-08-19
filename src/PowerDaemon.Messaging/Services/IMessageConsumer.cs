namespace PowerDaemon.Messaging.Services;

public interface IMessageConsumer
{
    Task StartConsumingAsync<T>(string queueName, Func<T, MessageContext, Task<bool>> messageHandler, CancellationToken cancellationToken = default) where T : class;
    Task StartConsumingAsync<T>(string queueName, string exchange, string routingKey, Func<T, MessageContext, Task<bool>> messageHandler, CancellationToken cancellationToken = default) where T : class;
    Task StopConsumingAsync(string queueName);
    Task<T?> ReceiveAsync<T>(string queueName, TimeSpan timeout, CancellationToken cancellationToken = default) where T : class;
}

public class MessageContext
{
    public string MessageId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string ReplyTo { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int DeliveryCount { get; set; }
    public Dictionary<string, object> Headers { get; set; } = new();
    public string RoutingKey { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    
    public void Acknowledge() => IsAcknowledged = true;
    public void Reject(bool requeue = false) 
    {
        IsRejected = true;
        RequeueOnReject = requeue;
    }
    
    internal bool IsAcknowledged { get; private set; }
    internal bool IsRejected { get; private set; }
    internal bool RequeueOnReject { get; private set; }
}