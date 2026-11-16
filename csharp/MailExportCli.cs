#!/usr/bin/env dotnet

#:property AssemblyName=dotmail
#:property Version=0.1.0
#:property EnableConfigurationBindingGenerator=true
#:property StripSymbols=true

#:property InvariantGlobalization=true
#:property OptimizationPreference=Size
#:property EventSourceSupport=false

#:package ConsoleAppFramework@5.8.1
#:package MailKit@5.0.0
#:package PeachPDF@0.10.0

#:include helpers/MicrosoftHelper.cs
#:include helpers/TelegramHelper.cs

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ConsoleAppFramework;
using Dotfiles.Helpers;
using Dotfiles.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using PeachPDF;
using static Helper;

await ConsoleApp.RunAsync(args, Exporter.ExportAsync);

static class Exporter {
    /// <summary>Export emails attached to matching messages as PDF files named after the message subject.</summary>
    /// <param name="mailSubject">Word or phrase the email subject must contain.</param>
    /// <param name="mailDateFrom">First day to search (yyyy-MM-dd). Default: today.</param>
    /// <param name="mailDateTo">Last day to search, inclusive (yyyy-MM-dd). Default: today.</param>
    /// <param name="mailUsername">IMAP username. Env: MAIL_USERNAME.</param>
    /// <param name="mailPassword">IMAP password, used when no client ID is given. Env: MAIL_PASSWORD.</param>
    /// <param name="mailClientId">Microsoft app (client) ID; switches to Microsoft sign-in (OAuth2). Env: MAIL_CLIENT_ID.</param>
    /// <param name="mailHost">IMAP host. Env: MAIL_HOST. Default: outlook.office365.com.</param>
    /// <param name="mailPort">IMAP port. Env: MAIL_PORT. Default: 993.</param>
    /// <param name="mailSecure">Use implicit TLS (true) or STARTTLS (false). Env: MAIL_SECURE. Default: true.</param>
    /// <param name="mailFolder">Folder to search. Env: MAIL_FOLDER. Default: INBOX.</param>
    /// <param name="authTimeout">Minutes to wait for the Telegram sign-in answer. Default: 720.</param>
    /// <param name="output">-o, Directory to write PDF files into. Default: current directory.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<int> ExportAsync(
        string mailSubject,
        string? mailDateFrom = null, string? mailDateTo = null,
        string? mailUsername = null, string? mailPassword = null, string? mailClientId = null,
        string? mailHost = null, int? mailPort = null, string? mailSecure = null, string? mailFolder = null,
        int authTimeout = 720, string output = ".",
        CancellationToken cancellationToken = default) {
        var username = EnvFallback(mailUsername, "MAIL_USERNAME")
                       ?? throw new UsageException("Missing username: pass --mail-username or set MAIL_USERNAME.");
        var clientId = EnvFallback(mailClientId, "MAIL_CLIENT_ID");
        var password = EnvFallback(mailPassword, "MAIL_PASSWORD");
        if (clientId is null && password is null) {
            await WriteError(
                "Missing credential: pass --mail-client-id (Microsoft sign-in) or --mail-password, or set MAIL_CLIENT_ID/MAIL_PASSWORD.");
            return 1;
        }

        var host = EnvFallback(mailHost, "MAIL_HOST") ?? "outlook.office365.com";
        var port = EnvFallbackInt(mailPort, "MAIL_PORT") ?? 993;
        var secure = EnvFallBackBool(mailSecure, "MAIL_SECURE") ?? true;
        var folderName = mailFolder ?? Env("MAIL_FOLDER") ?? "INBOX";
        var from = ParseDate(mailDateFrom);
        var to = ParseDate(mailDateTo);
        if (from > to) {
            await WriteError($"--mail-from ({from:yyyy-MM-dd}) is after --mail-to ({to:yyyy-MM-dd}).");
            return 1;
        }

        var telegramResult = TelegramHelper.BuildClient(timeoutSeconds: 100);
        if (telegramResult is not Result<TelegramCodes>.Success<TelegramClient> successful) {
            await WriteError($"Unable to create Telegram client: {telegramResult.Error}");
            return 1;
        }

        using var telegram = successful.Value;
        try {
            await using var client = new ImapClient();
            await client.ConnectAsync(host, port,
                secure ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancellationToken);
            if (clientId is null) {
                await client.AuthenticateAsync(username, password!, cancellationToken);
            }
            else {
                var signIn = new MicrosoftSignIn(telegram, clientId, username, telegram,
                    TimeSpan.FromMinutes(authTimeout));
                var accessToken = await signIn.GetAccessTokenAsync(cancellationToken);
                await client.AuthenticateAsync(new SaslMechanismOAuth2(username, accessToken), cancellationToken);
            }

            var exported =
                await ExportFolderAsync(client, folderName, mailSubject, from, to, output, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            Console.WriteLine($"Done: {exported} PDF file(s) written.");
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            await WriteError(ex is UsageException ? ex.Message : ex.ToString());
            await telegram.SendAsync($"❌ export_attachment failed: {ex.Message}", CancellationToken.None);
            return 1;
        }

        async Task WriteError(string message) => await Console.Error.WriteLineAsync($"❌ {message}", cancellationToken);
    }

    static async Task<int> ExportFolderAsync(ImapClient client, string folderName, string subject, DateOnly from,
        DateOnly to, string output, CancellationToken cancellationToken) {
        var folder = await client.GetFolderAsync(folderName, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var query = SearchQuery.SubjectContains(subject)
            .And(SearchQuery.DeliveredAfter(from.ToDateTime(TimeOnly.MinValue)))
            .And(SearchQuery.DeliveredBefore(to.ToDateTime(TimeOnly.MinValue).AddDays(1)));
        var uids = await folder.SearchAsync(query, cancellationToken);
        Console.WriteLine(
            $"Found {uids.Count} email(s) with subject containing \"{subject}\" between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.");
        Directory.CreateDirectory(output);
        var exported = 0;
        foreach (var uid in uids) {
            var message = await folder.GetMessageAsync(uid, cancellationToken);
            var attachedEmails = AttachedEmails(message).ToList();
            if (attachedEmails.Count == 0) {
                Console.WriteLine($"Skipped \"{message.Subject}\" ({message.Date:yyyy-MM-dd}): no attached email.");
                continue;
            }

            foreach (var attachedEmail in attachedEmails) {
                var path = UniquePath(output, FileNameOf(message.Subject));
                await RenderPdfAsync(attachedEmail, path);
                Console.WriteLine($"Exported \"{attachedEmail.Subject}\" to {path}");
                exported++;
            }
        }

        return exported;
    }

    static DateOnly ParseDate(string? value) => value is null
        ? DateOnly.FromDateTime(DateTime.Today)
        : DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static IEnumerable<MimeMessage> AttachedEmails(MimeMessage message) {
        foreach (var attachment in message.Attachments) {
            switch (attachment) {
                case MessagePart { Message: { } attached }:
                    yield return attached;

                    break;
                case MimePart part when part.FileName?.EndsWith(".eml", StringComparison.OrdinalIgnoreCase) == true:
                    using (var stream = new MemoryStream()) {
                        part.Content?.DecodeTo(stream);
                        stream.Position = 0;
                        yield return MimeMessage.Load(stream);
                    }

                    break;
                case MimePart part when part.FileName?.EndsWith(".msg", StringComparison.OrdinalIgnoreCase) == true:
                    Console.WriteLine($"Skipped attachment \"{part.FileName}\": Outlook .msg format is not supported.");
                    break;
            }
        }
    }

    static string FileNameOf(string? subject) {
        var name = string.IsNullOrWhiteSpace(subject) ? "no_subject" : subject.Trim();
        name = Regex.Replace(name, @"\s+", "_");
        foreach (var invalid in Path.GetInvalidFileNameChars().Append(':'))
            name = name.Replace(invalid, '_');
        return name;
    }

    static string UniquePath(string directory, string name) {
        var path = Path.Combine(directory, $"{name}.pdf");
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(directory, $"{name}_{i}.pdf");
        return path;
    }

    static async Task RenderPdfAsync(MimeMessage email, string path) {
        var config = new PdfGenerateConfig {
            PageSize = PageSize.A4,
            PageOrientation = PageOrientation.Portrait,
            MarginTop = 30,
            MarginBottom = 30,
            MarginLeft = 30,
            MarginRight = 30,
            AllowLocalFileAccess = false,
        };
        var document = await new PdfGenerator().GeneratePdf(ToHtml(email), config);
        await using var file = File.Create(path);
        document.Save(file);
    }

    static string ToHtml(MimeMessage email) {
        var body = email.HtmlBody is { } html
            ? InlineImages(html, email)
            : $"<pre style=\"white-space:pre-wrap;font-family:inherit\">{WebUtility.HtmlEncode(email.TextBody ?? string.Empty)}</pre>";
        var header = new StringBuilder()
            .Append(
                "<table style=\"font-family:sans-serif;font-size:12px;border-bottom:1px solid #999;margin-bottom:12px;width:100%\">")
            .Append(HeaderRow("From", email.From.ToString()))
            .Append(HeaderRow("To", email.To.ToString()))
            .Append(email.Cc.Count > 0 ? HeaderRow("Cc", email.Cc.ToString()) : string.Empty)
            .Append(HeaderRow("Date", email.Date.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)))
            .Append(HeaderRow("Subject", email.Subject ?? string.Empty))
            .Append("</table>")
            .ToString();
        var bodyTag = Regex.Match(body, @"<body[^>]*>", RegexOptions.IgnoreCase);
        return bodyTag.Success
            ? body.Insert(bodyTag.Index + bodyTag.Length, header)
            : $"<html><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(email.Subject)}</title></head><body>{header}{body}</body></html>";
    }

    static string HeaderRow(string label, string value) =>
        $"<tr><th style=\"text-align:left;width:70px;vertical-align:top\">{label}</th><td>{WebUtility.HtmlEncode(value)}</td></tr>";

    static string InlineImages(string html, MimeMessage email) {
        var parts = email.BodyParts.OfType<MimePart>().Where(part => !string.IsNullOrEmpty(part.ContentId)).ToList();
        return Regex.Replace(html, @"cid:([^""'\s)>]+)", match => {
            var contentId = Uri.UnescapeDataString(match.Groups[1].Value);
            var part = parts.FirstOrDefault(p =>
                string.Equals(p.ContentId, contentId, StringComparison.OrdinalIgnoreCase));
            if (part is null)
                return match.Value;

            using var stream = new MemoryStream();
            part.Content?.DecodeTo(stream);
            return $"data:{part.ContentType.MimeType};base64,{Convert.ToBase64String(stream.ToArray())}";
        }, RegexOptions.IgnoreCase);
    }
}

sealed class UsageException(string message) : Exception(message);

file static class Helper {
    public static string? EnvFallback(string? value, string env) => value ?? Env(env);

    public static int? EnvFallbackInt(int? value, string env) =>
        value ?? (Env(env) is { Length: > 0 } envValue && int.TryParse(envValue, out var parsedValue)
            ? parsedValue
            : null);

    public static bool? EnvFallBackBool(string? value, string env) => (value ?? Env(env)) switch {
        null => null,
        var envValue when bool.TryParse(envValue, out var parsedBool) => parsedBool,
        var envValue when int.TryParse(envValue, out var parsedInt) => parsedInt == 1,
        _ => false
    };

    public static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}
