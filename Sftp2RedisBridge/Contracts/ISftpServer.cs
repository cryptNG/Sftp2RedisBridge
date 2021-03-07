using System;
using System.Collections.Generic;
using Sftp2RedisBridge.Networking;

namespace Sftp2RedisBridge.Contracts
{
    public interface ISftpServer : IDisposable
    {

        SftpConfiguration Configuration { get; }
        bool Connected { get; }
        string CurrentDirectory { get; }
        string LocalDirectory { get; set; }
        bool CreateFile(string path,string fileName, byte[] fileContent);

        bool ChangeDirectory(string destinationDirectory);
        void MoveFile(string fileFrom, string directoryTo);
        void Connect();
        void Disconnect();
        bool DeleteFile(string filePath);
        RemoteFile DownloadFile(string fileName);
        byte[] DownloadFileContent(string fileName);
        void Kill();
        List<string> ListDirectory(string dirName);
    }
}