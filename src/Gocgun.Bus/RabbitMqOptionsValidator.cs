// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

using Microsoft.Extensions.Options;

/// <summary>
/// RabbitMQ seçeneklerini başlangıçta doğrular. Boş kimlik bilgisi kabul edilmez.
/// </summary>
internal sealed class RabbitMqOptionsValidator : IValidateOptions<RabbitMqOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.HostName)
            || string.IsNullOrWhiteSpace(options.UserName)
            || string.IsNullOrWhiteSpace(options.Password)
            || string.IsNullOrWhiteSpace(options.ServiceName)
            || string.IsNullOrWhiteSpace(options.Exchange))
        {
            return ValidateOptionsResult.Fail("RabbitMQ host, user, password, service name, and exchange are required.");
        }

        if (options.Port is < 1 or > 65535)
        {
            return ValidateOptionsResult.Fail("RabbitMQ port is out of range.");
        }

        if (options.PublisherChannelPoolSize < 1 || options.ConsumerConnectionCount < 1 || options.MaxConcurrency < 1)
        {
            return ValidateOptionsResult.Fail("RabbitMQ pool, consumer connection, and concurrency counts must be at least 1.");
        }

        if (options.PrefetchCount is < 1 or > ushort.MaxValue || options.DeliveryLimit < 1)
        {
            return ValidateOptionsResult.Fail("RabbitMQ prefetch or delivery limit is out of range.");
        }

        if (options.PublishConfirmTimeout <= TimeSpan.Zero || options.ShutdownTimeout < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("RabbitMQ timeouts are out of range.");
        }

        return ValidateOptionsResult.Success;
    }
}
