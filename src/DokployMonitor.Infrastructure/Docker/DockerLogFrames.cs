using System.Text;

namespace DokployMonitor.Infrastructure.Docker;

/// <summary>
/// Docker Engine log akisini metne cevirir.
///
/// TTY'si olmayan container/servislerde akis "multiplexed"dir: her karenin basinda
/// 8 byte baslik bulunur — 1 byte akis turu (0=stdin, 1=stdout, 2=stderr), 3 byte
/// dolgu (sifir), 4 byte big-endian govde uzunlugu. TTY'li container'larda baslik
/// yoktur, icerik ham metindir. Iki durumu ayirt edip ikisini de destekler.
/// </summary>
public static class DockerLogFrames
{
    /// <summary>Ham yaniti satirlara cevirir (bos satirlar atilir, CR kirpilir).</summary>
    public static List<string> ToLines(byte[] payload)
    {
        var text = IsMultiplexed(payload) ? Decode(payload) : Encoding.UTF8.GetString(payload);

        return [.. text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)];
    }

    /// <summary>Ilk kare basligi gecerli mi? (tur 0-2 ve 3 byte dolgu sifir)</summary>
    public static bool IsMultiplexed(byte[] payload) =>
        payload.Length >= 8 && payload[0] <= 2 && payload[1] == 0 && payload[2] == 0 && payload[3] == 0;

    private static string Decode(byte[] payload)
    {
        var output = new StringBuilder();
        var position = 0;

        while (position + 8 <= payload.Length)
        {
            var size = (payload[position + 4] << 24)
                | (payload[position + 5] << 16)
                | (payload[position + 6] << 8)
                | payload[position + 7];

            position += 8;

            if (size <= 0)
            {
                continue;
            }

            // Akis yarida kesilmis olabilir: yalnizca elimizdeki kadarini al.
            var length = Math.Min(size, payload.Length - position);
            output.Append(Encoding.UTF8.GetString(payload, position, length));
            position += length;
        }

        return output.ToString();
    }
}
