using System;
using System.Security.Cryptography;
using System.Text;
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
            if (sftpFile != null) TransactionUid = CreateTransportHashString(sftpFile.Attributes.LastWriteTimeUtc, sftpFile.FullName);
            FullName = sftpFile.FullName;
            Name = sftpFile.Name;
        }

        public static string CreateTransportHashString(DateTime timeStamp, string fullFileName)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in CreateTransportHash(timeStamp.ToLongDateString() + timeStamp.ToLongTimeString() +" "+ fullFileName))
                sb.Append(b.ToString("X2")); //X2 means format as hexadecimal string

            return sb.ToString();
        }

        public static byte[] CreateTransportHash(string inputString)
        {
            using (HashAlgorithm algorithm = SHA256.Create())
                return algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
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
                return (FullName == rf.FullName);
            }
        }
    }
}