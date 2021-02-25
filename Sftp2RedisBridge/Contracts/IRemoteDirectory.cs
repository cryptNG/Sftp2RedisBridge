using System;
using System.Collections.Generic;
using Sftp2RedisBridge.Networking;

namespace Sftp2RedisBridge.Contracts
{
    public interface IRemoteDirectory : IDisposable
    {
        List<RemoteFile> RemoteFiles { get; }
        bool CreateErrorFile(string fileName, byte[] content);
        bool Connect();
        bool DeleteFile(RemoteFile remoteFile);
        void Disconnect();
        void MoveFile(RemoteFile remoteFile, string toDirectory);
        RemoteFile GetFile(string fileName);
    }
}