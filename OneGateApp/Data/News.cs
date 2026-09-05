using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeoOrder.OneGate.Data;

public class News : IComparable<News>, IShareable, ICachedEntity
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }
    [MaxLength(16)]
    public required string Category { get; set; }
    [MaxLength(8)]
    [Unicode(false)]
    public required string Language { get; set; }
    public required string Guid { get; set; }
    public DateTimeOffset PublishDate { get; set; }
    [Url]
    public required string Url { get; set; }
    public required string Title { get; set; }
    public required string Authors { get; set; }
    public required string[] Keywords { get; set; }
    public required string Summary { get; set; }
    [Url]
    public string? ImageUrl { get; set; }
    public required string Content { get; set; }

    public string Subtitle => $"{PublishDate.LocalDateTime} {Authors}";
    public string ContentHtml => $$"""
        <!DOCTYPE html>

        <html xmlns="http://www.w3.org/1999/xhtml">
        <head>
            <meta charset="utf-8" />
            <title>{{Title}}</title>
            <style type="text/css">
                img { max-width: 100% !important; height: auto !important; }
            </style>
        </head>
        <body>
            {{Content}}
        </body>
        </html>
        """;

    string IShareable.Text => Title;
    string IShareable.Uri => $"https://{SharedOptions.OneGateDomain}/news/{Id}";

    int IComparable<News>.CompareTo(News? other)
    {
        if (other is null) return 1;
        int comparison = -PublishDate.CompareTo(other.PublishDate);
        return comparison != 0 ? comparison : Id.CompareTo(other.Id);
    }
}
