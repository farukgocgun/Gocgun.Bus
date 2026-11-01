// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// What the consume pipeline should do after filters run.
/// </summary>
public enum ConsumeDecision
{
    /// <summary>
    /// Run the handler when it has not run yet (a filter that returns this without calling next lets the pipeline run it), then acknowledge.
    /// </summary>
    Continue = 0,

    /// <summary>
    /// Acknowledge without treating the delivery as a failure.
    /// </summary>
    Ack = 1,

    /// <summary>
    /// Dead-letter without running the handler when the filter did not call next.
    /// </summary>
    DeadLetter = 2,
}
