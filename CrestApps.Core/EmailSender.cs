using CrestApps.Core.Service;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using System;
using System.Threading.Tasks;

namespace CrestApps.Core
{
    public class EmailSender : IEmailSender
    {
        private readonly EmailSenderOption Option;
        private readonly ILogger<EmailSender> Logger;

        public EmailSender(IOptions<EmailSenderOption> option, ILogger<EmailSender> logger)
        {
            Option = option.Value;
            Logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            MimeMessage message = GetMessage(subject, htmlMessage, email, string.Empty, true);

            await ProcessMessageAsync(message);
        }

        protected async Task ProcessMessageAsync(MimeMessage message)
        {
            try
            {
                SmtpClient client = new SmtpClient();
                await client.ConnectAsync(Option.Host, Option.Port ?? 0, Option.UseSSL ?? false);

                //Remove any OAuth functionality as we won't be using it. 
                client.AuthenticationMechanisms.Remove("XOAUTH2");

                await client.AuthenticateAsync(Option.SenderUsername, Option.SenderPassword);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                client.Dispose();
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Failed to send and email. {e.Message}");
                Logger.LogDebug(e.Message, e);
            }
        }


        protected MimeMessage GetMessage(string subject, string body, string recipientEmail, string recipientName, bool isBodyHtml)
        {
            var message = new MimeMessage()
            {
                Subject = subject,
                Body = new TextPart(isBodyHtml ? TextFormat.Html : TextFormat.Plain)
                {
                    Text = body
                }
            };

            message.From.Add(new MailboxAddress(Option.SenderName, Option.SenderEmail));
            message.To.Add(new MailboxAddress(recipientName, recipientEmail));

            return message;
        }

    }
}
