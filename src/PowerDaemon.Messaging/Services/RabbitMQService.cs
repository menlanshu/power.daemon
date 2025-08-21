using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Messaging.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PowerDaemon.Messaging.Services;

public class RabbitMQService : IMessagePublisher, IMessageConsumer, IDisposable
{
    private readonly ILogger<RabbitMQService> _logger;
    private readonly RabbitMQConfiguration _config;
    private readonly object _connectionLock = new();
    private IConnection? _connection;
    private IModel? _channel;
    private readonly Dictionary<string, EventingBasicConsumer> _consumers = new();
    private bool _disposed;

    public RabbitMQService(ILogger<RabbitMQService> logger, IOptions<RabbitMQConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
        InitializeConnection();
    }

    public async Task PublishAsync<T>(T message, string routingKey, CancellationToken cancellationToken = default) where T : class
    {
        await PublishAsync(message, routingKey, _config.ExchangeName, cancellationToken);
    }

    public async Task PublishAsync<T>(T message, string routingKey, string exchange, CancellationToken cancellationToken = default) where T : class
    {
        await PublishAsync(message, routingKey, new MessageProperties(), cancellationToken);
    }

    public async Task PublishAsync<T>(T message, string routingKey, MessageProperties? properties, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            EnsureConnection();
            
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            var basicProperties = _channel!.CreateBasicProperties();
            basicProperties.Persistent = properties?.Persistent ?? true;
            basicProperties.MessageId = properties?.MessageId ?? Guid.NewGuid().ToString();
            basicProperties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            basicProperties.Priority = properties?.Priority ?? 0;
            
            if (properties?.CorrelationId != null)
                basicProperties.CorrelationId = properties.CorrelationId;
            
            if (properties?.ReplyTo != null)
                basicProperties.ReplyTo = properties.ReplyTo;
                
            if (properties?.Expiration != null)
                basicProperties.Expiration = ((DateTimeOffset)properties.Expiration.Value).ToUnixTimeMilliseconds().ToString();

            if (properties?.Headers?.Count > 0)
                basicProperties.Headers = new Dictionary<string, object>(properties.Headers);

            _channel.BasicPublish(
                exchange: _config.ExchangeName,
                routingKey: routingKey,
                basicProperties: basicProperties,
                body: body);

            _logger.LogDebug("Published message {MessageType} to {RoutingKey}", typeof(T).Name, routingKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message {MessageType} to {RoutingKey}", typeof(T).Name, routingKey);
            throw;
        }

        await Task.CompletedTask;
    }

    public async Task PublishBatchAsync<T>(IEnumerable<T> messages, string routingKey, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            EnsureConnection();
            
            var batch = _channel!.CreateBasicPublishBatch();
            
            foreach (var message in messages)
            {
                var json = JsonSerializer.Serialize(message);
                var body = Encoding.UTF8.GetBytes(json);

                var basicProperties = _channel.CreateBasicProperties();
                basicProperties.Persistent = true;
                basicProperties.MessageId = Guid.NewGuid().ToString();
                basicProperties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                batch.Add(_config.ExchangeName, routingKey, false, basicProperties, body.AsMemory());
            }

            batch.Publish();
            _logger.LogDebug("Published {Count} messages of type {MessageType} to {RoutingKey}", 
                messages.Count(), typeof(T).Name, routingKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish batch messages {MessageType} to {RoutingKey}", typeof(T).Name, routingKey);
            throw;
        }

        await Task.CompletedTask;
    }

    public async Task StartConsumingAsync<T>(string queueName, Func<T, MessageContext, Task<bool>> messageHandler, CancellationToken cancellationToken = default) where T : class
    {
        await StartConsumingAsync(queueName, _config.ExchangeName, queueName, messageHandler, cancellationToken);
    }

    public async Task StartConsumingAsync<T>(string queueName, string exchange, string routingKey, Func<T, MessageContext, Task<bool>> messageHandler, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            EnsureConnection();
            EnsureQueueExists(queueName, exchange, routingKey);

            if (_consumers.ContainsKey(queueName))
            {
                _logger.LogWarning("Consumer for queue {QueueName} already exists", queueName);
                return;
            }

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var json = Encoding.UTF8.GetString(body);
                    var message = JsonSerializer.Deserialize<T>(json);

                    if (message == null)
                    {
                        _logger.LogWarning("Failed to deserialize message from queue {QueueName}", queueName);
                        _channel!.BasicReject(ea.DeliveryTag, false);
                        return;
                    }

                    var context = new MessageContext
                    {
                        MessageId = ea.BasicProperties.MessageId ?? string.Empty,
                        CorrelationId = ea.BasicProperties.CorrelationId ?? string.Empty,
                        ReplyTo = ea.BasicProperties.ReplyTo ?? string.Empty,
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds(ea.BasicProperties.Timestamp.UnixTime).DateTime,
                        DeliveryCount = GetDeliveryCount(ea.BasicProperties.Headers),
                        Headers = ea.BasicProperties.Headers?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new(),
                        RoutingKey = ea.RoutingKey,
                        Exchange = ea.Exchange
                    };

                    var success = await messageHandler(message, context);

                    if (success || context.IsAcknowledged)
                    {
                        _channel!.BasicAck(ea.DeliveryTag, false);
                    }
                    else if (context.IsRejected)
                    {
                        _channel!.BasicReject(ea.DeliveryTag, context.RequeueOnReject);
                    }
                    else
                    {
                        // Default: requeue for retry
                        _channel!.BasicReject(ea.DeliveryTag, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message from queue {QueueName}", queueName);
                    _channel!.BasicReject(ea.DeliveryTag, false);
                }
            };

            _consumers[queueName] = consumer;
            _channel!.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
            
            _logger.LogInformation("Started consuming messages from queue {QueueName}", queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start consuming from queue {QueueName}", queueName);
            throw;
        }

        await Task.CompletedTask;
    }

    public async Task StopConsumingAsync(string queueName)
    {
        if (_consumers.TryGetValue(queueName, out var consumer))
        {
            _consumers.Remove(queueName);
            _logger.LogInformation("Stopped consuming messages from queue {QueueName}", queueName);
        }

        await Task.CompletedTask;
    }

    public async Task<T?> ReceiveAsync<T>(string queueName, TimeSpan timeout, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            EnsureConnection();

            // Use a small delay to make this properly async
            await Task.Delay(1, cancellationToken);

            var result = _channel!.BasicGet(queueName, false);
            if (result == null)
                return null;

            var body = result.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var message = JsonSerializer.Deserialize<T>(json);

            _channel.BasicAck(result.DeliveryTag, false);
            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to receive message from queue {QueueName}", queueName);
            return null;
        }
    }

    private void InitializeConnection()
    {
        try
        {
            var factory = new ConnectionFactory()
            {
                HostName = _config.HostName,
                Port = _config.Port,
                UserName = _config.UserName,
                Password = _config.Password,
                VirtualHost = _config.VirtualHost,
                RequestedHeartbeat = TimeSpan.FromSeconds(_config.RequestedHeartbeat),
                NetworkRecoveryInterval = TimeSpan.FromSeconds(_config.NetworkRecoveryInterval),
                AutomaticRecoveryEnabled = _config.AutomaticRecoveryEnabled
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Declare main exchange
            _channel.ExchangeDeclare(_config.ExchangeName, ExchangeType.Topic, durable: true);
            
            // Declare dead letter exchange
            _channel.ExchangeDeclare(_config.DeadLetterExchange, ExchangeType.Direct, durable: true);

            _logger.LogInformation("RabbitMQ connection established successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RabbitMQ connection");
            throw;
        }
    }

    private void EnsureConnection()
    {
        lock (_connectionLock)
        {
            if (_connection == null || !_connection.IsOpen || _channel == null || !_channel.IsOpen)
            {
                _logger.LogWarning("RabbitMQ connection lost, reconnecting...");
                InitializeConnection();
            }
        }
    }

    private void EnsureQueueExists(string queueName, string exchange, string routingKey)
    {
        var queueArgs = new Dictionary<string, object>
        {
            ["x-message-ttl"] = _config.MessageTtlSeconds * 1000,
            ["x-dead-letter-exchange"] = _config.DeadLetterExchange
        };

        _channel!.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false, arguments: queueArgs);
        _channel.QueueBind(queueName, exchange, routingKey);
    }

    private static int GetDeliveryCount(IDictionary<string, object>? headers)
    {
        if (headers?.TryGetValue("x-delivery-count", out var countObj) == true && countObj is int count)
        {
            return count;
        }
        return 1;
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            foreach (var consumer in _consumers.Values)
            {
                // Consumers are automatically disposed when channel is closed
            }
            _consumers.Clear();

            _channel?.Close();
            _channel?.Dispose();
            
            _connection?.Close();
            _connection?.Dispose();
            
            _logger.LogInformation("RabbitMQ connection disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing RabbitMQ connection");
        }
        finally
        {
            _disposed = true;
        }
    }
}