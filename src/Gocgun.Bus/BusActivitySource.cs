// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

using System.Diagnostics;

/// <summary>
/// Activity source named Gocgun.Bus for publish and consume spans.
/// </summary>
public static class BusActivitySource
{
    /// <summary>
    /// Gets the source hosts register with OpenTelemetry.
    /// </summary>
    public static ActivitySource Source { get; } = new("Gocgun.Bus");
}
