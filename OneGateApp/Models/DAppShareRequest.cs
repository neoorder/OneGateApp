namespace NeoOrder.OneGate.Models;

record DAppShareRequest
{
    public string? Title { get; init; }
    public string? Text { get; init; }
    public string? Uri { get; init; }
    public string? Subject { get; init; }
}
