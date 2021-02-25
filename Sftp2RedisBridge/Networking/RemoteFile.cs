using System;
using Renci.SshNet.Sftp;

namespace Sftp2RedisBridge.Networking
{
    public class RemoteFile
    {
        public RemoteFile()
        {
        }

        public RemoteFile(SftpFile sftpFile, byte[] data)
        {
            if (sftpFile != null) TransactionUid = Guid.NewGuid().ToString();
            FullName = sftpFile.FullName;
            Name = sftpFile.Name;
            Data = data;
        }

        public string TransactionUid { get; set; }
        public string FullName { get; set; }
        public string Name { get; set; }
        public byte[] Data { get; set; }
    }
}