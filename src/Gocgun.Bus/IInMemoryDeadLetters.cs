// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// Inspectable dead-letter list for the in-memory transport.
/// </summary>
public interface IInMemoryDeadLetters
{
    /// <summary>
    /// Gets a snapshot of dead letters, oldest first.
    /// </summary>
    /// <returns>The current dead letters.</returns>
    IReadOnlyList<DeadLetter> Snapshot();
}
