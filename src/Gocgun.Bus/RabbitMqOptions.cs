// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// RabbitMQ taşıma ayarları. Kimlik bilgisi için varsayılan yoktur; boş ana makine, kullanıcı veya parola başlangıçta hata verir.
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>
    /// Gets or sets broker ana makine adı. Boş olamaz.
    /// </summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets aMQP bağlantı noktası. Varsayılan 5672.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// Gets or sets sanal konak. Varsayılan "/".
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Gets or sets broker kullanıcı adı. Boş olamaz. guest kullanılmaz.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets broker parolası. Boş olamaz.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether tLS açık mı. Varsayılan kapalı.
    /// </summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// Gets or sets kuyruk adı öneki. Boş olamaz.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets olay topic exchange adı. Varsayılan gocgun.bus.
    /// </summary>
    public string Exchange { get; set; } = "gocgun.bus";

    /// <summary>
    /// Gets or sets tüketici prefetch. Varsayılan 16.
    /// </summary>
    public int PrefetchCount { get; set; } = 16;

    /// <summary>
    /// Gets or sets yayımcı onayının beklenme süresi. Varsayılan 10 saniye.
    /// </summary>
    public TimeSpan PublishConfirmTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets quorum kuyruk x-delivery-limit değeri. Varsayılan 20.
    /// </summary>
    public int DeliveryLimit { get; set; } = 20;

    /// <summary>
    /// Gets or sets bağlantı adı öneki. Varsayılan gocgun.
    /// </summary>
    public string ConnectionNamePrefix { get; set; } = "gocgun";

    /// <summary>
    /// Gets or sets ana uygulamanın güvendiği yayımcı kullanıcı adı. Bu paket kiracı yorumu yapmaz; değer konak filtreleri içindir.
    /// </summary>
    public string TrustedTenantPublisherUser { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets tüketici bağlantı sayısı. Varsayılan 1.
    /// </summary>
    public int ConsumerConnectionCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets yayımcı kanal havuzu boyutu. Varsayılan 4.
    /// </summary>
    public int PublisherChannelPoolSize { get; set; } = 4;

    /// <summary>
    /// Gets or sets İşleyici başına eşzamanlı tüketim. Varsayılan 1.
    /// </summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>
    /// Gets or sets kapanışta uçuştaki iş için üst süre. Varsayılan 15 saniye.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
