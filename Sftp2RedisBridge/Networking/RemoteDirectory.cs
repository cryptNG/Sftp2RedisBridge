using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using log4net;
using Microsoft.Extensions.Configuration;
using Sftp2RedisBridge.Contracts;

namespace Sftp2RedisBridge.Networking
{
    public class RemoteDirectory : IRemoteDirectory
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
        private readonly IRedisPipelineManager _cache;

        private readonly IConfiguration _configuration;

        private readonly ISftpServer _server;

        //Can redis contain data that does not exist on sftp server?
        public RemoteDirectory(ISftpServer sftpServer, IConfiguration configuration, IRedisPipelineManager cache)
        {
            _cache = cache;
            _server = sftpServer;


            _configuration = configuration;
        }

        public string CurrentDirectory => _server.CurrentDirectory;

        public void Dispose()
        {
            _server.Dispose();
        }


        public List<RemoteFile> RemoteFiles
        {
            get
            {
                UpdateFiles();

                return _cache.GetCachedFiles();
            }
        }

        public bool Connect()
        {
            log.Info("Connecting");
            _server.Connect();
           // UpdateFiles();
            return _server.Connected;
        }

        public void Disconnect()
        {
            _server.Disconnect();
        }

        public RemoteFile GetFile(string fileName)
        {
            return _server.DownloadFile(fileName);
        }

        public bool DeleteFile(RemoteFile remoteFile)
        {
            _server.DeleteFile(remoteFile.FullName);
            //  _cache.DeleteCachedFile(remoteFile);
            // UpdateFiles();

            return true;
        }

        public bool CreateErrorFile(string fileName, byte[] content)
        {
            _server.CreateFile(_server.Configuration.ErrorPath,fileName, content);
            return true;
        }

        public void MoveFile(RemoteFile remoteFile, string toDirectory)
        {
            _server.MoveFile(remoteFile.FullName, toDirectory);
        }

        public bool UpdateFiles()
        {
            log.Debug("Polling for SFTP files and updating local cash");
            var downloadedFiles = new List<RemoteFile>();
            var cachedFiles = _cache.GetCachedFiles();

            foreach (var s in _server.ListDirectory(CurrentDirectory))
            {
                log.Debug("Found file on server: " + s);
                if (!cachedFiles.Any(x => x.FullName == s)) //dont want new tuids on every call
                {
                    downloadedFiles.Add(_server.DownloadFile(s));
                    log.Debug("Adding File " + s + " to cache");
                }
                else
                {
                    
                    log.Debug("File " + s + " is already cached locally");
                }
            }

            if (downloadedFiles.Any())
                _cache.SetOrUpdateCachedFiles(downloadedFiles);

            return true;
        }
    }
}