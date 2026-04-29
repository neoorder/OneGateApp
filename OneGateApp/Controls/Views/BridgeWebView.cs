using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView : WebView
{
    public event EventHandler<BridgeWebView, JsonObject>? InvokedFromJavaScript;

    public string? BridgeToken { get; set; }

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
            if (BridgeToken is not { Length: > 0 } token) return;
            if (request["onegateBridgeToken"] is not JsonValue bridgeToken || bridgeToken.GetValueKind() != JsonValueKind.String || bridgeToken.GetValue<string>() != token) return;
            request.Remove("onegateBridgeToken");
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
}
