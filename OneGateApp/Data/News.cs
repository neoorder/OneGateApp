using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Net;

namespace NeoOrder.OneGate.Data;

public class News : IComparable<News>, IShareable
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

    public string Subtitle
    {
        get
        {
            string published = PublishDate.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
            return string.IsNullOrWhiteSpace(Authors) ? published : $"{published} - {Authors}";
        }
    }
    public string ContentHtml
    {
        get
        {
            string title = WebUtility.HtmlEncode(Title);
            string authors = WebUtility.HtmlEncode(Authors);
            string published = WebUtility.HtmlEncode(PublishDate.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));
            string summary = WebUtility.HtmlEncode(Summary);
            string hero = string.IsNullOrWhiteSpace(ImageUrl)
                ? ""
                : $"""
                    <figure class="hero">
                        <img src="{WebUtility.HtmlEncode(ImageUrl)}" alt="{title}" />
                    </figure>
                  """;
            string metadata = string.IsNullOrWhiteSpace(Authors) ? published : $"{published} &middot; {authors}";
            string summaryBlock = string.IsNullOrWhiteSpace(Summary)
                ? ""
                : $$"""
                            <p class="summary">{{summary}}</p>
                  """;

            return $$"""
                <!DOCTYPE html>

                <html xmlns="http://www.w3.org/1999/xhtml">
                <head>
                    <meta charset="utf-8" />
                    <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
                    <title>{{title}}</title>
                    <style type="text/css">
                        :root {
                            color-scheme: light dark;
                            --page: #f6f8fb;
                            --surface: #ffffff;
                            --text: #171a22;
                            --muted: #727b8f;
                            --border: #e5e9f0;
                            --link: #2563eb;
                        }

                        * {
                            box-sizing: border-box;
                        }

                        html {
                            background: var(--page);
                            -webkit-text-size-adjust: 100%;
                        }

                        body {
                            margin: 0;
                            background: var(--page);
                            color: var(--text);
                            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
                        }

                        .article {
                            max-width: 760px;
                            margin: 0 auto;
                            padding: 28px 22px 48px;
                        }

                        .masthead {
                            margin-bottom: 22px;
                        }

                        h1 {
                            margin: 0 0 14px;
                            color: var(--text);
                            font-size: 26px;
                            font-weight: 720;
                            line-height: 1.2;
                            letter-spacing: 0;
                        }

                        .meta {
                            margin: 0 0 18px;
                            color: var(--muted);
                            font-size: 14px;
                            line-height: 1.45;
                        }

                        .summary {
                            margin: 0;
                            color: var(--muted);
                            font-size: 16px;
                            line-height: 1.55;
                        }

                        .hero {
                            margin: 0 0 24px;
                            overflow: hidden;
                            border: 1px solid var(--border);
                            border-radius: 18px;
                            background: var(--surface);
                        }

                        .hero img {
                            display: block;
                            width: 100%;
                            max-width: 100% !important;
                            height: auto !important;
                            aspect-ratio: 16 / 9;
                            object-fit: cover;
                        }

                        .content {
                            padding-top: 2px;
                            color: var(--text);
                            font-size: 17px;
                            line-height: 1.66;
                            overflow-wrap: anywhere;
                        }

                        .content > :first-child {
                            margin-top: 0 !important;
                        }

                        .content p,
                        .content ul,
                        .content ol,
                        .content blockquote,
                        .content figure {
                            margin: 0 0 1.15em;
                        }

                        .content h2,
                        .content h3 {
                            margin: 1.45em 0 0.55em;
                            line-height: 1.25;
                        }

                        .content h2 {
                            font-size: 23px;
                        }

                        .content h3 {
                            font-size: 20px;
                        }

                        .content a {
                            color: var(--link);
                            text-decoration-thickness: 1.5px;
                            text-underline-offset: 3px;
                        }

                        .content img,
                        .content video,
                        .content iframe {
                            max-width: 100% !important;
                            height: auto !important;
                            border-radius: 14px;
                        }

                        .content blockquote {
                            padding: 2px 0 2px 16px;
                            border-left: 3px solid var(--border);
                            color: var(--muted);
                        }

                        @media (prefers-color-scheme: dark) {
                            :root {
                                --page: #0f131a;
                                --surface: #171d26;
                                --text: #f3f5f8;
                                --muted: #a3adbb;
                                --border: #2b3340;
                                --link: #8ab4ff;
                            }
                        }
                    </style>
                </head>
                <body>
                    <main class="article">
                        {{hero}}
                        <header class="masthead">
                            <h1>{{title}}</h1>
                            <p class="meta">{{metadata}}</p>
                {{summaryBlock}}
                        </header>
                        <article class="content">
                            {{Content}}
                        </article>
                    </main>
                </body>
                </html>
                """;
        }
    }

    string IShareable.Text => Title;
    string IShareable.Uri => $"https://{SharedOptions.OneGateDomain}/news/{Id}";

    int IComparable<News>.CompareTo(News? other)
    {
        if (other is null) return 1;
        return -PublishDate.CompareTo(other.PublishDate);
    }
}
