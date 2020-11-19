namespace CrestApps.Core.Service
{
    public class EmailSenderOption
    {
        public string Host { get; set; }
        public int? Port { get; set; }
        public string SenderEmail { get; set; }
        public string SenderName { get; set; }
        public string SenderUsername { get; set; }
        public string SenderPassword { get; set; }
        public bool? UseSSL { get; set; }

        public bool HasOptions => Host != default
                               || Port != default
                               || SenderEmail != default
                               || SenderName != default
                               || SenderUsername != default
                               || SenderPassword != default
                               || UseSSL != default;
    }
}
