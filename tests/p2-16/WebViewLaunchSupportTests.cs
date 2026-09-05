using NeoOrder.OneGate.Services;
using System.Xml.Linq;
using Xunit;

namespace OneGate.WebViewSupport.Tests;

public class WebViewLaunchSupportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaunchRequiresDocumentStartScript(bool supported)
    {
        WebViewLaunchSupport support = new(supported, "com.android.chrome");

        Assert.Equal(supported, support.CanLaunchDApp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task UnknownProviderUsesAndroidSystemWebView(string? package)
    {
        WebViewLaunchSupport support = new(false, package);
        Uri? opened = null;

        Assert.True(await support.OpenUpdateAsync(uri =>
        {
            opened = uri;
            return Task.FromResult(true);
        }));

        Assert.Equal(new Uri("market://details?id=com.google.android.webview"), opened);
    }

    [Fact]
    public async Task InstalledProviderIsOpenedWithoutTryingBrowserWhenMarketSucceeds()
    {
        WebViewLaunchSupport support = new(false, "com.android.chrome");
        List<Uri> opened = [];

        Assert.True(await support.OpenUpdateAsync(uri =>
        {
            opened.Add(uri);
            return Task.FromResult(true);
        }));

        Assert.Equal(new Uri("market://details?id=com.android.chrome"), Assert.Single(opened));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingMarketFallsBackToHttpsAndReportsResult(bool browserResult)
    {
        WebViewLaunchSupport support = new(false, "com.android.webview");
        List<Uri> opened = [];

        bool result = await support.OpenUpdateAsync(uri =>
        {
            opened.Add(uri);
            return Task.FromResult(opened.Count == 2 && browserResult);
        });

        Assert.Equal(browserResult, result);
        Assert.Collection(opened,
            uri => Assert.Equal(new Uri("market://details?id=com.android.webview"), uri),
            uri => Assert.Equal(new Uri("https://play.google.com/store/apps/details?id=com.android.webview"), uri));
    }

    [Fact]
    public async Task PackageValueCannotInjectAdditionalQueryArguments()
    {
        const string package = "provider&referrer=untrusted#fragment";
        WebViewLaunchSupport support = new(false, package);
        List<Uri> opened = [];

        await support.OpenUpdateAsync(uri =>
        {
            opened.Add(uri);
            return Task.FromResult(false);
        });

        Assert.All(opened, uri =>
        {
            Assert.Empty(uri.Fragment);
            Assert.Equal("?id=" + Uri.EscapeDataString(package), uri.Query);
        });
    }

    [Fact]
    public async Task LauncherExceptionsRemainAvailableToNativeErrorGuidance()
    {
        WebViewLaunchSupport support = new(false, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => support.OpenUpdateAsync(_ =>
            throw new InvalidOperationException("No foreground activity")));
    }

    [Fact]
    public void EverySupportedLocaleHasCompleteUpgradeGuidance()
    {
        string[] keys = ["WebViewUpdateRequired", "WebViewUpdateRequiredText", "UpdateWebView", "CloseDApp", "WebViewUpdateOpenFailed"];
        string[] resources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Resources"), "Strings*.resx");
        Assert.Equal(15, resources.Length);
        foreach (string path in resources)
        {
            XElement root = XDocument.Load(path).Root!;
            foreach (string key in keys)
            {
                XElement resource = Assert.Single(root.Elements("data"), element => (string?)element.Attribute("name") == key);
                Assert.False(string.IsNullOrWhiteSpace((string?)resource.Element("value")), $"Missing {key} in {Path.GetFileName(path)}");
            }
        }
    }
}
