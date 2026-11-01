// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests;

using System.Text;
using Gocgun.Bus.Internal;
using Gocgun.Bus.Tests.Messages;
using Gocgun.Bus.Tests.Support;
using Gocgun.Bus.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

/// <summary>
/// Shares one RabbitMQ container.
/// </summary>
[CollectionDefinition(RabbitCollection.Name)]
public sealed class RabbitCollection : ICollectionFixture<RabbitFixture>
{
    /// <summary>
    /// Collection name.
    /// </summary>
    public const string Name = "rabbit";
}
