namespace PowerDaemon.Messaging.Services;

public interface IMessagePublisher
{
    Task PublishAsync<T>(T message, string routingKey, CancellationToken cancellationToken = default) where T : class;
    Task PublishAsync<T>(T message, string routingKey, string exchange, CancellationToken cancellationToken = default) where T : class;
    Task PublishAsync<T>(T message, string routingKey, MessageProperties? properties, CancellationToken cancellationToken = default) where T : class;
    Task PublishBatchAsync<T>(IEnumerable<T> messages, string routingKey, CancellationToken cancellationToken = default) where T : class;
}

public class MessageProperties
{
    public string? MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public string? ReplyTo { get; set; }
    public DateTime? Expiration { get; set; }
    public byte Priority { get; set; } = 0;
    public bool Persistent { get; set; } = true;
    public Dictionary<string, object> Headers { get; set; } = new();
}