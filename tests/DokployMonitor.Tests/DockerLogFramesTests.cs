using System.Text;
using DokployMonitor.Infrastructure.Docker;

namespace DokployMonitor.Tests;

/// <summary>
/// Docker Engine log akisinin cozumlenmesi. Yanlis cozumleme kullaniciya bozuk
/// (icinde kontrol karakteri olan) log satirlari gosterir; bu yuzden kare
/// mantigi ayrica test edilir.
/// </summary>
public sealed class DockerLogFramesTests
{
    [Fact]
    public void Multiplexed_akis_satirlara_cevrilir()
    {
        var payload = Frames(
            (1, "Step 1/8 : FROM node:20-alpine\n"),
            (2, "npm ERR! code ELIFECYCLE\n"),
            (1, "Build finished\n"));

        Assert.True(DockerLogFrames.IsMultiplexed(payload));
        Assert.Equal(
            ["Step 1/8 : FROM node:20-alpine", "npm ERR! code ELIFECYCLE", "Build finished"],
            DockerLogFrames.ToLines(payload));
    }

    [Fact]
    public void Tek_karede_birden_fazla_satir_bolunur()
    {
        var payload = Frames((1, "birinci\nikinci\r\nucuncu\n"));

        Assert.Equal(["birinci", "ikinci", "ucuncu"], DockerLogFrames.ToLines(payload));
    }

    [Fact]
    public void TTY_akisinda_baslik_yoktur_ham_metin_okunur()
    {
        var payload = Encoding.UTF8.GetBytes("baslik yok\nikinci satir\n");

        Assert.False(DockerLogFrames.IsMultiplexed(payload));
        Assert.Equal(["baslik yok", "ikinci satir"], DockerLogFrames.ToLines(payload));
    }

    [Fact]
    public void Yarida_kesilen_kare_elde_olan_kadariyla_okunur()
    {
        var complete = Frames((1, "tam satir\n"), (1, "yarim satir kesildi"));

        // Son karenin govdesinin bir kismi eksik gelmis gibi kirp.
        var truncated = complete[..^6];

        var lines = DockerLogFrames.ToLines(truncated);
        Assert.Equal("tam satir", lines[0]);
        Assert.StartsWith("yarim satir", lines[1]);
    }

    [Fact]
    public void Bos_yanit_bos_liste_doner()
    {
        Assert.Empty(DockerLogFrames.ToLines([]));
        Assert.Empty(DockerLogFrames.ToLines(Frames((1, "\n\n"))));
    }

    /// <summary>Engine'in urettigi 8 byte baslikli kareleri olusturur.</summary>
    private static byte[] Frames(params (byte Stream, string Text)[] frames)
    {
        var output = new List<byte>();

        foreach (var (stream, text) in frames)
        {
            var body = Encoding.UTF8.GetBytes(text);
            output.AddRange([stream, 0, 0, 0]);
            output.AddRange([
                (byte)(body.Length >> 24),
                (byte)(body.Length >> 16),
                (byte)(body.Length >> 8),
                (byte)body.Length,
            ]);
            output.AddRange(body);
        }

        return [.. output];
    }
}
