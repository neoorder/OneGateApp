using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Models.AppLinks;

class ViewNewsAction : AppLinkAction
{
    protected override string Route => "//home/news/details";
    public Uri Uri { get; }
    public int NewsId { get; }

    public ViewNewsAction(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("URI must be absolute.", nameof(uri));
        if (uri.Scheme != "https")
            throw new ArgumentException("Invalid scheme for ViewNewsAction", nameof(uri));
        if (uri.Authority != SharedOptions.OneGateDomain)
            throw new ArgumentException("Invalid authority for ViewNewsAction", nameof(uri));
        if (uri.Segments is not ["/", "news/", var newsIdSegment])
            throw new ArgumentException("Invalid path for ViewNewsAction", nameof(uri));
        this.Uri = uri;
        NewsId = int.Parse(newsIdSegment);
        if (NewsId <= 0)
            throw new ArgumentException("Invalid news id", nameof(uri));
    }

    protected override Page CreatePage(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetServiceOrCreateInstance<NewsDetailsPage>();
    }

    protected override IDictionary<string, object> CreateQuery()
    {
        return new Dictionary<string, object>
        {
            ["uri"] = Uri
        };
    }

    public static new ViewNewsAction? TryCreate(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        if (Uri.TryCreate(uri, UriKind.Absolute, out var result))
            return TryCreate(result);
        return null;
    }

    public static new ViewNewsAction? TryCreate(Uri? uri)
    {
        if (uri is null) return null;
        try
        {
            return new(uri);
        }
        catch
        {
            return null;
        }
    }
}
