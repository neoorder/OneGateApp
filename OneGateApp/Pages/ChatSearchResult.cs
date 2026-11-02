using Meshline.Models.Client;
using NeoOrder.OneGate.Services.Messaging;

namespace NeoOrder.OneGate.Pages;

public sealed record ChatSearchResult(MessageInfo? Message, string Sender, string CreatedAt, FormattedString FormattedPreview)
{
    public ChatSearchResult(PendingChatMessage pending, string sender, string createdAt, FormattedString formattedPreview)
        : this((MessageInfo?)null, sender, createdAt, formattedPreview)
    {
        Pending = pending;
    }

    public PendingChatMessage? Pending { get; }
    public Guid? PendingId => Pending?.Id;
    public MessageRef? Key => Message?.Key;
    public DateTimeOffset Timestamp => Message?.CreatedAt ?? Pending!.CreatedAt;
    public string Preview => string.Concat(FormattedPreview.Spans.Select(span => span.Text));
}
