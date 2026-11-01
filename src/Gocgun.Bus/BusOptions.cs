// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// Her iki taşımanın ortak teslim sınırları.
/// </summary>
public sealed class BusOptions
{
    /// <summary>
    /// Gets or sets ölü harfe düşmeden önce süreç içi deneme sayısı. Gecikmeli yeniden deneme burada yapılmaz. Varsayılan 3.
    /// </summary>
    public int MaxImmediateRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets rabbitMQ prefetch değeri. RabbitMQ taşıması seçilince bu değer kullanılır. Varsayılan 16.
    /// </summary>
    public int PrefetchCount { get; set; } = 16;

    /// <summary>
    /// Gets or sets İşleyici başına eşzamanlı okuyucu sayısı. Varsayılan 1.
    /// </summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>
    /// Gets or sets kabul edilen gövde üst sınırı, bayt. Aşan yayımcı çağrısı hata verir; gelen aşırı gövde ölü harfe gider. Varsayılan 262144.
    /// </summary>
    public int MaxPayloadBytes { get; set; } = 262_144;

    /// <summary>
    /// Gets or sets envelope başlık sayısı üst sınırı. Varsayılan 32.
    /// </summary>
    public int MaxHeaderCount { get; set; } = 32;

    /// <summary>
    /// Gets or sets tek başlık değerinin karakter üst sınırı. Varsayılan 1024.
    /// </summary>
    public int MaxHeaderValueLength { get; set; } = 1024;
}
