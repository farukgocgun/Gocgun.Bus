# Gocgun.Bus

> **Beta.** This package is in beta: the public API may change between releases.

AGPLv3 library for messages that cross a process boundary.

License: AGPL-3.0-only (`LICENSE`). Copyright holder exception: `LICENSE-EXCEPTION`.

`IMessageBus.PublishAsync` publishes an integration event. `SendAsync` sends an integration command. Both wrap the payload in `gocgun.bus.v1.Envelope`. The wire name is the payload type URL. An unknown type is dead-lettered and no handler runs.

## Contracts

Messages are C# classes serialized with protobuf-net. No `.proto` files and no code generation. The contract name is required: it is the wire name and the RabbitMQ routing key, so renaming or moving the class does not change routing.

```csharp
[ProtoContract(Name = "crocovity.tools.v1.ToolUsed")]
public sealed class ToolUsed : IIntegrationEvent
{
    [ProtoMember(1)]
    public string Tool { get; set; } = string.Empty;
}
```

The envelope (`Gocgun.Bus.V1.Envelope`) keeps the proto3 wire layout: `sent_at` is a `google.protobuf.Timestamp`, `headers` a map, and the payload uses the `google.protobuf.Any` layout with a `type.googleapis.com/` type URL. A non-.NET consumer can export a schema with `Serializer.GetProto<ToolUsed>()`.

## Buses

Each bus has one transport:

- RabbitMQ (`RabbitMQ.Client`): one publisher connection, one consumer connection, a pooled confirm channel for publishes, one channel per handler queue.
- In-memory (`System.Threading.Channels`): one bounded channel per handler. A full channel waits. Dead letters are visible through `IInMemoryDeadLetters`.

A host can run several buses at once: any number of RabbitMQ buses (for example two brokers) plus at most one in-memory bus. Call `AddGocgunBus` once per bus. `AddGocgunBus(configure)` registers the bus named `default`; `AddGocgunBus(name, configure)` registers a named bus.

```csharp
services.AddGocgunBus(bus => bus
    .UseInMemory()
    .AddHandler<CacheCleared, CacheClearedHandler>());

services.AddGocgunBus("orders", bus => bus
    .UseRabbitMq(options => configuration.GetSection("Buses:orders:RabbitMq").Bind(options))
    .AddHandler<OrderPlaced, OrderPlacedHandler>()
    .AddPublishFilter<TenantHeaderFilter>());

services.AddGocgunBus("analytics", bus => bus
    .UseRabbitMq(options => configuration.GetSection("Buses:analytics:RabbitMq").Bind(options))
    .AddHandler<ToolUsed, ToolUsedHandler>());
```

Resolving a bus:

- The `default` bus is `IMessageBus`, and also the keyed service `"default"`.
- A named bus is the keyed service under its name: `[FromKeyedServices("orders")] IMessageBus orders` or `provider.GetRequiredKeyedService<IMessageBus>("orders")`.
- Without a `default` bus there is no unkeyed `IMessageBus`.

Routing is per bus. A publish goes only through the bus it is called on. A handler consumes only from the bus it was added to; add it to more than one bus to consume from each. Publish filters, consume filters and the idempotency store registered on a builder apply only to that bus (they are keyed services under the bus name).

Startup fails when:

- two buses share a name;
- a second in-memory bus is added;
- two RabbitMQ buses use the same host, port, virtual host, exchange and service name (they would consume the same queues);
- a bus name is empty, longer than 64 characters, or has characters other than ASCII letters, digits, `.`, `-` and `_`;
- one builder selects two transports.

RabbitMQ connection names include the bus name for every bus except `default`. Health checks are `gocgun-bus-rabbitmq` for the `default` bus and `gocgun-bus-rabbitmq-<name>` for the others.

## Configuration

The package does not read configuration. `appsettings.Gocgun.Bus.json` shows the shape a host binds: one section per bus under `Buses:<name>`, with `Transport` (`InMemory` or `RabbitMq`), `Bus` (shared limits), and `RabbitMq` or `InMemoryBus`.

RabbitMQ credentials have no defaults. Startup fails when the host name, user, or password is empty.

The package has no tenant field and no outbox type. It does not reference `Gocgun.Mediator`. Durable outbox and tenant filters stay in the host.

## Publishing to nuget.org

Run `scripts/publish.sh` (or `scripts/publish.ps1` on Windows). It dispatches the manual `release.yml` workflow of each dependency first, in dependency order (Gocgun.Bus), then of this package, waiting for each run and stopping on the first failure. Requires the GitHub CLI signed in with `gh auth login`; the `nuget` environment and the nuget.org Trusted Publishing policy must exist in each repository.
