using DokployMonitor.Core.Dokploy;

namespace DokployMonitor.Core.Abstractions;

/// <summary>
/// Belirli bir Dokploy baglantisi (API anahtari + adres) icin istemci uretir.
///
/// Her baglantinin kendi anahtari ve sertifika politikasi oldugu icin istemci tek bir
/// yapilandirmaya baglanamaz. Uretilen istemci **uzun sure saklanmamalidir**: HttpClient
/// fabrikasinin handler omru gecerse baglanti havuzu tazelenmez.
/// </summary>
public interface IDokployClientFactory
{
    IDokployClient Create(DokployConnection connection);
}
