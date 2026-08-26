using System.Text;
using System.Text.Json;
using LoreAI.Infrastructure.Gmail;

namespace LoreAI.Infrastructure.Tests.Gmail;

public class GmailMessageParserTests
{
    [Fact]
    public void Parse_SinglePartPlainText_ReturnsSubjectBodyAndInlinedUrl()
    {
        var payload = BuildPayload(
            subject: "Ma newsletter .NET",
            mimeType: "text/plain",
            content: "Lisez cet article ( https://blog.example.com/article ). Bonne lecture !");

        var result = GmailMessageParser.Parse(payload);

        Assert.Equal("Ma newsletter .NET", result.Subject);
        Assert.Contains("Lisez cet article", result.Body);
        Assert.Contains("https://blog.example.com/article", result.CandidateUrls);
    }

    [Fact]
    public void Parse_MultipartAlternative_PrefersPlainTextOverHtml()
    {
        var payload = BuildMultipartAlternativePayload(
            subject: "Digest",
            plainText: "Version texte avec https://blog.example.com/plain-link",
            html: "<html><body>Version HTML avec <a href=\"https://blog.example.com/html-link\">un lien</a></body></html>");

        var result = GmailMessageParser.Parse(payload);

        Assert.Contains("Version texte", result.Body);
        Assert.Contains("https://blog.example.com/plain-link", result.CandidateUrls);
        Assert.DoesNotContain("https://blog.example.com/html-link", result.CandidateUrls);
    }

    [Fact]
    public void Parse_HtmlOnlyMessage_StripsTagsAndExtractsHrefUrls()
    {
        var payload = BuildPayload(
            subject: "Digest HTML",
            mimeType: "text/html",
            content: "<html><body><p>Un vrai article &amp; plus</p><a href=\"https://blog.example.com/html-article\">Lire</a></body></html>");

        var result = GmailMessageParser.Parse(payload);

        Assert.Contains("Un vrai article & plus", result.Body);
        Assert.DoesNotContain('<', result.Body);
        Assert.Contains("https://blog.example.com/html-article", result.CandidateUrls);
    }

    [Fact]
    public void Parse_NoTextPart_ReturnsEmptyBodyAndNoUrls()
    {
        var payload = JsonDocument.Parse("""{ "headers": [], "mimeType": "image/png", "body": { "data": "" } }""").RootElement;

        var result = GmailMessageParser.Parse(payload);

        Assert.Equal(string.Empty, result.Body);
        Assert.Empty(result.CandidateUrls);
    }

    [Fact]
    public void ExtractCandidateUrls_DuplicateUrls_ReturnsDistinctList()
    {
        var body = "Voir https://blog.example.com/a et encore https://blog.example.com/a.";

        var urls = GmailMessageParser.ExtractCandidateUrls(body);

        Assert.Single(urls);
    }

    [Fact]
    public void ExtractCandidateUrls_TrailingPunctuation_IsTrimmed()
    {
        var body = "Article ici: https://blog.example.com/a, et là (https://blog.example.com/b).";

        var urls = GmailMessageParser.ExtractCandidateUrls(body);

        Assert.Contains("https://blog.example.com/a", urls);
        Assert.Contains("https://blog.example.com/b", urls);
    }

    private static JsonElement BuildPayload(string subject, string mimeType, string content)
    {
        var json = $$"""
            {
                "headers": [ { "name": "Subject", "value": {{JsonSerializer.Serialize(subject)}} } ],
                "mimeType": {{JsonSerializer.Serialize(mimeType)}},
                "body": { "data": {{JsonSerializer.Serialize(EncodeBase64Url(content))}} }
            }
            """;

        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement BuildMultipartAlternativePayload(string subject, string plainText, string html)
    {
        var json = $$"""
            {
                "headers": [ { "name": "Subject", "value": {{JsonSerializer.Serialize(subject)}} } ],
                "mimeType": "multipart/alternative",
                "parts": [
                    { "mimeType": "text/plain", "body": { "data": {{JsonSerializer.Serialize(EncodeBase64Url(plainText))}} } },
                    { "mimeType": "text/html", "body": { "data": {{JsonSerializer.Serialize(EncodeBase64Url(html))}} } }
                ]
            }
            """;

        return JsonDocument.Parse(json).RootElement;
    }

    private static string EncodeBase64Url(string content) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(content)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
