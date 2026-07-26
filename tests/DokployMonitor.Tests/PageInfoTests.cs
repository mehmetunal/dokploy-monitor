using DokployMonitor.Web.Models;

namespace DokployMonitor.Tests;

/// <summary>
/// Paging bounds: user input arrives from the query string, so out-of-range values must be
/// clamped rather than producing empty pages or negative offsets.
/// </summary>
public sealed class PageInfoTests
{
    [Fact]
    public void Varsayilanlar_ilk_sayfayi_verir()
    {
        var page = PageInfo.Create(null, null, 120);

        Assert.Equal(1, page.Page);
        Assert.Equal(PageInfo.DefaultSize, page.Size);
        Assert.Equal(0, page.Skip);
        Assert.False(page.HasPrevious);
        Assert.True(page.HasNext);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(99, 3)]
    public void Sayfa_numarasi_siniri_asamaz(int requested, int expected)
    {
        Assert.Equal(expected, PageInfo.Create(requested, 50, 120).Page);
    }

    [Fact]
    public void Desteklenmeyen_sayfa_boyutu_varsayilana_duser()
    {
        Assert.Equal(PageInfo.DefaultSize, PageInfo.Create(1, 7, 100).Size);
        Assert.Equal(25, PageInfo.Create(1, 25, 100).Size);
    }

    [Fact]
    public void Bos_liste_tek_sayfa_sayilir()
    {
        var page = PageInfo.Create(3, 25, 0);

        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.PageCount);
        Assert.Equal(0, page.FirstRow);
        Assert.Equal(0, page.LastRow);
        Assert.False(page.HasNext);
    }

    [Fact]
    public void Satir_araligi_dogru_hesaplanir()
    {
        var page = PageInfo.Create(3, 25, 120);

        Assert.Equal(50, page.Skip);
        Assert.Equal(51, page.FirstRow);
        Assert.Equal(75, page.LastRow);
        Assert.Equal(5, page.PageCount);
    }
}
