// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

using System.Threading.Channels;

/// <summary>
/// Bellek içi kanal sınırları. Taşıma kalıcı değildir.
/// </summary>
public sealed class InMemoryBusOptions
{
    /// <summary>
    /// Gets or sets İşleyici başına kanal kapasitesi. Varsayılan 1024.
    /// </summary>
    public int Capacity { get; set; } = 1024;

    /// <summary>
    /// Gets or sets kanal doluyken davranış. Varsayılan beklemedir.
    /// </summary>
    public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.Wait;

    /// <summary>
    /// Gets or sets a value indicating whether dropWrite, DropOldest veya DropNewest için açık onay. Varsayılan kapalı.
    /// </summary>
    public bool AllowLossyFullMode { get; set; }

    /// <summary>
    /// Gets or sets kapanışta okuyucuların boşaltılması için üst süre. Varsayılan 5 saniye.
    /// </summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
