using System.Reflection;
using CTFServer.Models.Internal;
using CTFServer.Services.Interface;
using CTFServer.Utils;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CTFServer.Services;

public class MailSender : IMailSender
{
    private readonly EmailConfig? options;
    private readonly ILogger<MailSender> logger;
    private readonly IStringLocalizer<ServiceResource> loc;

    public MailSender(IOptions<EmailConfig> options, ILogger<MailSender> logger,
        IStringLocalizer<ServiceResource> _loc)
    {
        loc = _loc;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<bool> SendEmailAsync(string subject, string content, string to)
    {
        if (options?.SendMailAddress is null ||
            options?.Smtp?.Host is null ||
            options?.Smtp?.Port is null)
            return true;

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(options.SendMailAddress, options.SendMailAddress));
        msg.To.Add(new MailboxAddress(to, to));
        msg.Subject = subject;
        msg.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = content };

        try
        {
            using var client = new SmtpClient();

            await client.ConnectAsync(options.Smtp.Host, options.Smtp.Port.Value);
            client.AuthenticationMechanisms.Remove("XOAUTH2");
            await client.AuthenticateAsync(options.UserName, options.Password);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);

            logger.SystemLog(loc["Sent email to: "] + to, TaskStatus.Success, LogLevel.Information);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, loc["Error occurred while sending email"]);
            return false;
        }
    }

    public async Task SendUrlAsync(string? title, string? information, string? btnmsg, string? userName, string? email, string? url)
    {
        if (email is null || userName is null || title is null)
        {
            logger.SystemLog(loc["Bad email sending call!"], TaskStatus.Fail);
            return;
        }

        string ns = typeof(MailSender).Namespace ?? "CTFServer.Services";
        Assembly asm = typeof(MailSender).Assembly;
        string resourceName = $"{ns}.Assets.URLEmailTemplate.html";
        string emailContent = await
            new StreamReader(asm.GetManifestResourceStream(resourceName)!)
            .ReadToEndAsync();
        emailContent = emailContent
            .Replace("{title}", title)
            .Replace("{information}", information)
            .Replace("{btnmsg}", btnmsg)
            .Replace("{email}", email)
            .Replace("{userName}", userName)
            .Replace("{url}", url)
            .Replace("{nowtime}", DateTimeOffset.UtcNow.ToString("u"));
        if (!await SendEmailAsync(title, emailContent, email))
            logger.SystemLog(loc["Failed to send email!"], TaskStatus.Fail);
    }

    private bool SendUrlIfPossible(string? title, string? information, string? btnmsg, string? userName, string? email, string? url)
    {
        if (options?.SendMailAddress is null)
            return false;

        var _ = SendUrlAsync(title, information, btnmsg, userName, email, url);
        return true;
    }

    public bool SendConfirmEmailUrl(string? userName, string? email, string? confirmLink)
        => SendUrlIfPossible(loc["Confirm your email"],
            string.Format(loc["You are in the process of account registration operation, " +
                "we need to verify your registered email: {0}, " +
                "please click the button below to verify."], email),
            loc["Confirm"], userName, email, confirmLink);

    public bool SendChangeEmailUrl(string? userName, string? email, string? resetLink)
        => SendUrlIfPossible(loc["Change your email"],
            loc["You are in the process of changing your account email address, " +
                "please click the button below to verify your new email address."],
            $"{loc["Confirm"]} {loc["Change your email"].ToString().ToLower()}", userName, email, resetLink);

    public bool SendResetPasswordUrl(string? userName, string? email, string? resetLink)
        => SendUrlIfPossible(loc["Reset your password"],
            loc["You are in the process of resetting your account password, " +
                "please click the button below to reset your password."],
            $"{loc["Confirm"]} {loc["Reset your password"].ToString().ToLower()}", userName, email, resetLink);
}
