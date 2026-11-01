// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests;

/// <summary>
/// The package stays free of mediator, tenant, and outbox types.
/// </summary>
public sealed class PackageBoundaryTests
{
    /// <summary>
    /// Referenced assemblies and type names stay inside the bus contract.
    /// </summary>
    [Fact]
    public void AssemblyHasNoMediatorTenantOrOutbox()
    {
        var assembly = typeof(IMessageBus).Assembly;
        var names = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.DoesNotContain(names, name => name != null && name.Contains("Mediator", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name != null && name.Contains("MassTransit", StringComparison.Ordinal));
        Assert.Contains(names, name => string.Equals(name, "RabbitMQ.Client", StringComparison.Ordinal));
        Assert.Contains(names, name => string.Equals(name, "protobuf-net.Core", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => string.Equals(name, "Google.Protobuf", StringComparison.Ordinal));
        foreach (var type in assembly.GetTypes())
        {
            Assert.DoesNotContain("Outbox", type.Name, StringComparison.Ordinal);
            Assert.DoesNotContain("Tenant", type.Name, StringComparison.Ordinal);
            foreach (var property in type.GetProperties())
            {
                Assert.NotEqual("TenantId", property.Name, StringComparer.Ordinal);
            }
        }
    }
}
