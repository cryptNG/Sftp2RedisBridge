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
        }

        public string TransactionUid { get; set; }
        public string FullName { get; set; }
        public string Name { get; set; }

        public override bool Equals(Object obj)
        {
            if ((obj == null) || !this.GetType().Equals(obj.GetType()))
            {
                return false;
            }
            else
            {
                RemoteFile rf = (RemoteFile)obj;
                return (FullName == rf.FullName) && (TransactionUid == rf.TransactionUid);
            }
        }
    }
}