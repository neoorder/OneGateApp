using System.Text.Json;
using System.Text.Json.Nodes;
using NeoOrder.OneGate.Controls.Handlers;

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView : WebView
{
    public static readonly BindableProperty DocumentStartScriptProperty = BindableProperty.Create(
        nameof(DocumentStartScript),
        typeof(string),
        typeof(BridgeWebView),
        null,
        propertyChanged: OnDocumentStartScriptChanged);

    public event EventHandler<BridgeWebView, JsonObject>? InvokedFromJavaScript;

    public string? DocumentStartScript
    {
        get => (string?)GetValue(DocumentStartScriptProperty);
        set => SetValue(DocumentStartScriptProperty, value);
    }

    internal void OnMessage(string payload)
    {
        JsonObject? request;
        try
        {
            if (string.IsNullOrWhiteSpace(payload)) return;
            request = JsonNode.Parse(payload) as JsonObject;
            if (request is null) return;
            if (request["jsonrpc"] is not JsonValue jsonrpc || jsonrpc.GetValueKind() != JsonValueKind.String || jsonrpc.GetValue<string>() != "2.0") return;
            if (request["method"]?.GetValueKind() != JsonValueKind.String) return;
            if (request["params"] is not null && request["params"] is not JsonArray) return;
            if (request["id"] is not JsonValue id) return;
            if (id.GetValueKind() != JsonValueKind.String && id.GetValueKind() != JsonValueKind.Number) return;
        }
        catch
        {
            return;
        }
        if (MainThread.IsMainThread)
            InvokedFromJavaScript?.Invoke(this, request);
        else
            MainThread.BeginInvokeOnMainThread(() => InvokedFromJavaScript?.Invoke(this, request));
    }

    static void OnDocumentStartScriptChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is BridgeWebView { Handler: BridgeWebViewHandler handler })
            handler.UpdateDocumentStartScript();
    }
}
