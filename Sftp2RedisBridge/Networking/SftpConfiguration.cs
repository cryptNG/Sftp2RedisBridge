namespace Sftp2RedisBridge.Networking
{
    public class SftpConfiguration
    {
        public SftpConfiguration(string Username, string PrivateKeyFile, string KeyFilePassword, string Host,
            string Port, string SftpPath, string ErrorPath)
        {
            this.Username = Username;
            this.PrivateKeyFile = PrivateKeyFile;
            this.KeyFilePassword = KeyFilePassword;
            this.Host = Host;
            this.Port = Port;
            this.SftpPath = SftpPath;
            this.ErrorPath = ErrorPath;
        }

        public string ErrorPath { get; set; }
        public string SftpPath { get; set; }
        public string Host { get; set; }
        public string Port { get; set; }
        public string Username { get; set; }

        public string PrivateKeyFile { get; set; }
        public string KeyFilePassword { get; set; }
    }
}