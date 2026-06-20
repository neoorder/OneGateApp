using Neo;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;
using System.Web;

namespace NeoOrder.OneGate.Models.AppLinks;

class PaymentAction : AppLinkAction
{
    protected override string Route => "//wallet/send";
    public string Recipient { get; }
    public UInt160? AssetId { get; }
    public decimal? Amount { get; }

    public PaymentAction(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("URI must be absolute.", nameof(uri));
        if (uri.Scheme != "neo")
            throw new ArgumentException("Invalid scheme for PaymentAction", nameof(uri));
        Recipient = uri.LocalPath;
        var nv = HttpUtility.ParseQueryString(uri.Query);
        if (nv["asset"] is string s_asset)
            AssetId = UInt160.Parse(s_asset);
        if (nv["amount"] is string s_amount)
            Amount = decimal.Parse(s_amount);
    }

    protected override IDictionary<string, object> CreateQuery()
    {
        var query = new Dictionary<string, object>
        {
            ["address"] = Recipient
        };
        if (AssetId is not null) query["asset"] = AssetId;
        if (Amount.HasValue) query["amount"] = Amount.Value;
        return query;
    }

    protected override Page CreatePage(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetServiceOrCreateInstance<SendPage>();
    }

    public static new PaymentAction? TryCreate(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        if (Uri.TryCreate(uri, UriKind.Absolute, out var result))
            return TryCreate(result);
        return null;
    }

    public static new PaymentAction? TryCreate(Uri? uri)
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
