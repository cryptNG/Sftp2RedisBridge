using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using log4net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Renci.SshNet;
using Renci.SshNet.Common;
using Sftp2RedisBridge.Contracts;

namespace Sftp2RedisBridge.Networking
{
    public class SftpServer : ISftpServer
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private readonly SftpClient _client;
        private readonly IConfiguration _globalConfiguration;
        public string LocalDirectory { get; set; }


        public string CurrentDirectory => _client.WorkingDirectory;
        public SftpConfiguration Configuration { get; }
        public bool Connected => _client.IsConnected;

        public SftpServer(IConfiguration globalConfiguration)
        {
            _globalConfiguration = globalConfiguration;


            var Configuration = new SftpConfiguration
            (globalConfiguration["SftpConnection:Auth:Username"],
                globalConfiguration["SftpConnection:Auth:PrivateKeyFile"],
                globalConfiguration["SftpConnection:Auth:KeyFilePassword"],
                globalConfiguration["SftpConnection:Host"],
                globalConfiguration["SftpConnection:Port"],
                globalConfiguration["SftpConnection:SftpPath"],
                globalConfiguration["SftpConnection:ErrorPath"]);
            LocalDirectory = Configuration.SftpPath;
            this.Configuration = Configuration;


            if (!File.Exists(globalConfiguration["SftpConnection:Auth:PrivateKeyFile"]))
                log.Error("Private key file: " + globalConfiguration["SftpConnection:Auth:PrivateKeyFile"] +
                          " not found!");
            PrivateKeyAuthenticationMethod privateKeyAuthentication = null;
            try
            {
                 privateKeyAuthentication =
                    new PrivateKeyAuthenticationMethod(
                        Configuration.Username, new PrivateKeyFile[] { new PrivateKeyFile(Configuration.PrivateKeyFile, Configuration.KeyFilePassword) });

            }
            catch (Exception e)
            {
                log.Fatal("Could not find file: "+ Configuration.PrivateKeyFile + " please check DEBUG log for details");
                log.Debug("EXCEPTION: ", e);
               //TODO: IHOSTAPPLICATIONLIFETIME
                throw;
            }
          

            var connectionInfo =
                new ConnectionInfo(
                    Configuration.Host,
                    Convert.ToInt32(Configuration.Port),
                    Configuration.Username, privateKeyAuthentication);

            _client = new SftpClient(connectionInfo);

            log.Debug("HOST: " + connectionInfo.Host + " PORT: " + connectionInfo.Port + " USER: " +
                      connectionInfo.Username + " AUTHMETOD: " + connectionInfo.AuthenticationMethods[0].Username);
        }

   

        public bool DeleteFile(string fileName)
        {
            if (_client.Exists(fileName))
            {
                try
                {
                    _client.DeleteFile(fileName);
                }
                catch (SftpPermissionDeniedException sftpEx)
                {
                    log.Error(
                        $"An SFTP Permission exception occurred while trying to delete the remote file {fileName}",
                        sftpEx);
                    log.Error(
                        "Check SFTP User permissions for: " + _globalConfiguration["SftpConnection:Auth:Username"]);
                    throw;
                }

                return true;
            }

            log.Warn(
                $"The file {fileName} we tried to delete from the server did not exist, did the host owner (or some other thread) move or delete the file?");
            return false;
        }

        public bool ChangeDirectory(string destinationDirectory)
        {
            if (_client.Exists(destinationDirectory))
            {
                _client.ChangeDirectory(destinationDirectory);
                return true;
            }

            return false;
        }

        public RemoteFile DownloadFile(string fileName)
        {

            if (!_client.IsConnected)
            {
                log.Error("CLIENT NOT CONNECTED TO RECEIVE " + fileName);
            }

                RemoteFile remoteFile;
                using (var downloadStream = new MemoryStream())
                {
                    _client.DownloadFile(fileName, downloadStream);
                    remoteFile = new RemoteFile(_client.Get(fileName), downloadStream.ToArray());
                    log.Info($"FOUND REMOTE FILE: {remoteFile.TransactionUid}:{remoteFile.Name}");
                    return remoteFile;
                }
          

        }


        public List<string> ListDirectory(string dirName)
        {
            var files = new List<string>();
            foreach (var entry in _client.ListDirectory(dirName))
            {

                log.Debug("Directorylisting: DIRECTORY " + entry.FullName);
                if (entry.IsRegularFile)
                {
                    files.Add(entry.FullName);
                    log.Debug("Directorylisting: FILE " + entry.FullName);
                }
            }

            return files;
        }

        public void MoveFile(string fileFrom, string directoryTo)
        {
            try
            {
                

                log.Debug($"Requested MOVE FILE FROM {fileFrom} TO {directoryTo}");
                log.Debug($"WorkingDir {_client.WorkingDirectory}");
                var inFile = _client.Get(fileFrom);
                inFile.MoveTo($"{directoryTo}/{inFile.Name}");
            }
            catch (SftpPathNotFoundException sftpPathEx)
            {
                log.Debug(sftpPathEx);
                log.Error($"File move from {fileFrom} to {directoryTo} failed!");
                log.Error("Please check the directory path, check for missing forward-slashes, if the file has been deleted on the server and check if the homedir is configured correctly.");
                log.Error("This file is going to be forgot by the system");
            }
        }

        public bool CreateFile(string path,string filename, byte[] fileContent)
        {
            string uploadPath = $"{path}/{filename}";
            using MemoryStream memStream = new MemoryStream();
            memStream.Write(fileContent, 0, fileContent.Length);
            memStream.Seek(0, SeekOrigin.Begin);
            _client.UploadFile(memStream, uploadPath);
            return true;
        }

        public void Connect()
        {
            try
            {
                if (!_client.IsConnected)
                    _client.Connect();
                _client.ChangeDirectory(Configuration.SftpPath);
            }
            catch (SftpPathNotFoundException spathEx)
            {
                log.Error("SftpPathNotFoundException occurred. " +
                          "This might mean that the configured remote path (config) " +
                          "is incorrect or missing a leading forwardslash." +
                          "It might also point to the remote server having deleted the path you want to access.");
                log.Error("Current remote directory: " + _client.WorkingDirectory);
                log.Error("Tried to change to: " + Configuration.SftpPath);
                log.Error("Exception", spathEx);
            }
            catch (Exception ex)
            {
                log.Error("An exception occurred while trying to connect or change the sftp directory", ex);
                log.Error("SFTPServer might not be available");
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }

        public void Disconnect()
        {
            log.Info("Disconnecting from SFTP Server");
            _client.Disconnect();
        }

        public void Kill()
        {
            _client.Disconnect();
            _client.Dispose();
        }
    }
}