using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Renci.SshNet.Common;
using Sftp2RedisBridge.Contracts;
using Sftp2RedisBridge.Networking;
using StackExchange.Redis;

namespace Sftp2RedisBridge
{
    public class Worker : BackgroundService
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
        private readonly IConfiguration _configuration;
        private readonly IRedisPipelineManager _redisPipelineManager;
        private readonly IRemoteDirectory _remoteDirectory;

        public Worker(IConfiguration configuration, IRedisPipelineManager redisPipelineManager,
            IRemoteDirectory remoteDirectory)
        {
            _configuration = configuration;
            _redisPipelineManager = redisPipelineManager;
            _remoteDirectory = remoteDirectory;
            _sftpBackgroundWorker = new BackgroundWorker();
            bool redisReady = false;
            while (redisReady == false)
            {
                redisReady = ConfigureBackgroundWorkers();
            }
        }


        public BackgroundWorker _sftpBackgroundWorker { get; set; }

      

        public string MakeBase64Chunky(byte[] fileData)
        {
            StringBuilder builder = new StringBuilder();
            using (var inputStream = new MemoryStream(fileData))
            {
                byte[] buffer = new byte[2187];
                int bytesRead = inputStream.Read(buffer, 0, buffer.Length);
                while (bytesRead > 0)
                {
                    String base64String = Convert.ToBase64String(buffer, 0, bytesRead);
                    builder.Append(base64String);
                    bytesRead = inputStream.Read(buffer, 0, buffer.Length);
                }
            }

            return builder.ToString();
        }

        public bool ConfigureBackgroundWorkers()
        {
            _sftpBackgroundWorker.WorkerSupportsCancellation = true;
            _sftpBackgroundWorker.DoWork += SftpClientListen;
            _redisPipelineManager.DoneBackgroundWorker.RunWorkerCompleted += RedisDoneStreamReceived;
            _redisPipelineManager.ErrorBackgroundWorker.RunWorkerCompleted += RedisErrorStreamReceived;
            return _redisPipelineManager.Initialize();
        }


        private int getReconnectDelayValue()=>Convert.ToInt32(_configuration["General:ReconnectDelaySeconds"]??"10") * 1000; //TODO do it on every convert.to
        

        public void SftpClientListen(object sender, DoWorkEventArgs e)
        {

            int reconnectDelayDefault = getReconnectDelayValue();

            int retryTimeMs = reconnectDelayDefault;
            while (!_sftpBackgroundWorker.CancellationPending)
            {
                if (_remoteDirectory.Connect() == false)
                {
                
                        retryTimeMs = retryTimeMs + reconnectDelayDefault / (retryTimeMs / reconnectDelayDefault) ^ 2;
                    
                    Thread.Sleep(retryTimeMs);
                    continue;
                }

                


                foreach (var currFile in _remoteDirectory.RemoteFiles)
                {
                    try
                    {
                        log.Debug("Found TUID " + currFile.TransactionUid);
                        if (!_redisPipelineManager.Contains($"Process:{currFile.TransactionUid}:Filename"))
                        {log.Info("File " + currFile.FullName + " being added to KeyValueStore with TUID " + currFile.TransactionUid);
                           _redisPipelineManager.Set($"Process:{currFile.TransactionUid}:Filename", currFile.Name);
                           _redisPipelineManager.Set($"Process:{currFile.TransactionUid}:File", MakeBase64Chunky(currFile.Data));
                           _redisPipelineManager.WriteToProcessStream(currFile.TransactionUid);

                        }
                    }
                    catch (Exception exception)
                    {
                        log.Debug($"An error occurred while processing one of the SFTP Server files {currFile.FullName}, skipping for now", exception);
                        
                        log.Error($"An error occurred while processing one of the SFTP Server files {currFile.FullName}, skipping for now (SEE DEBUG LOGS FOR DETAILS)");
                       log.Error(exception.Message);
                    }

                }

                log.Debug("Delaying " + _configuration["SftpConnection:PollingDelaySeconds"] + " seconds");
                Thread.Sleep(Convert.ToInt32(_configuration["SftpConnection:PollingDelaySeconds"]) * 1000);
                //Do not disconnec after each loop
                //_remoteDirectory.Disconnect();
            }
        }

        public void RedisDoneStreamReceived(object sender, RunWorkerCompletedEventArgs e)
        {
            var messages = (List<RedisMessage>) e.Result;
            foreach (var message in messages)
            {
                try
                {
                    RemoteFile fileToDelete;
                    try
                    {
                        fileToDelete = _remoteDirectory.RemoteFiles.First(x => x.TransactionUid == message.TransactionID);
                    }
                    catch (Exception)
                    {

                        log.Error("Could not find matching file on Server for TRANSACTIONID " + message.TransactionID +
                                  ", but the file has been marked as DONE in the pipeline.");
                        continue;
                    }

                    _remoteDirectory.DeleteFile(fileToDelete);
                    ITransaction redisTransaction = _redisPipelineManager.Cache.CreateTransaction();
                    redisTransaction= _redisPipelineManager.Delete(redisTransaction, $"{fileToDelete.TransactionUid}:File");
                    redisTransaction= _redisPipelineManager.Delete(redisTransaction, $"{fileToDelete.TransactionUid}:Filename");
                    redisTransaction= _redisPipelineManager.AcknowledgeDone(redisTransaction,message);
                    redisTransaction.ExecuteAsync();
                    log.Info($"Deleted Remote file {fileToDelete.Name} from Server");
                }
                catch (Exception exception)
                {
                    log.Debug("An exception occurred while processing a REDIS DONE message received event (to delete a file from the KeyValue DB), skipping for now", exception);

                    log.Error("An exception occurred while processing a REDIS DONE message received event (to delete a file from the KeyValue DB), skipping for now (See DEBUG LOGS for details)");
                    log.Error($"This means that the REDIS Message with values: Redis-MessageId {message.RedisMessageId} TUID: {message.TransactionID} Content: {message.Content} will not be processed, but will be retried");
                    log.Error(exception.Message);
                }

               
            }

            
        }

        public void RedisErrorStreamReceived(object sender, RunWorkerCompletedEventArgs e)
        {
            var messages = (List<RedisMessage>) e.Result;
            foreach (var message in messages)
            {
                try
                {
                    RemoteFile fileToMove = null;
                    try
                    {

                        fileToMove = _remoteDirectory.RemoteFiles.First(x => x.TransactionUid == message.TransactionID);
                        log.Debug("Found file to move: " + fileToMove);

                    }
                    catch (SshException sshex)
                    {
                        log.Error($"Remote file for TUID {message.TransactionID} could not be found! See debug log for exception details");
                       log.Debug(sshex);
                       
                    }

                    try
                    {
                        if (fileToMove != null)
                            _remoteDirectory.MoveFile(fileToMove, _configuration["SftpConnection:ErrorPath"]);
                    }catch(SshException sshExc) when (sshExc.StackTrace.Contains("Renci.SshNet.Sftp.SftpSession.RequestRename"))
                    {
                        log.Warn($"Remote file for TUID {message.TransactionID} name {fileToMove} exists already in error path {_configuration["SftpConnection:ErrorPath"]}. Deleting source file.");
                        _remoteDirectory.DeleteFile(fileToMove);
                        log.Debug($"Remote origin file for TUID {message.TransactionID} name {fileToMove} deleted.");

                    }

                    string errorFileNameKey = $"Error:{message.TransactionID}:Filename";
                    string errorFileContentKey = $"Error:{message.TransactionID}:File";
                  
                    if (_redisPipelineManager.Cache.KeyExists(errorFileNameKey) && _redisPipelineManager.Cache.KeyExists(errorFileContentKey))
                    {
                        byte[] errorFileContent = null;
                        string errorFileName = null;
                        string b64Content = null;
                        errorFileName = _redisPipelineManager.Get(errorFileNameKey);
                        b64Content = _redisPipelineManager.Get(errorFileContentKey);

                        if(!String.IsNullOrEmpty(b64Content))
                            errorFileContent = Convert.FromBase64String(b64Content);

                        if (!String.IsNullOrEmpty(errorFileName) && errorFileContent != null &&
                            errorFileContent.Length != 0)
                        {
                            if (!_remoteDirectory.CreateErrorFile(errorFileName, errorFileContent))
                            {
                                log.Error($"The remote file with Name: {errorFileName} and Content: {b64Content} could not be generated successfully");
                            }
                        }
                    }
                    else
                    {
                        log.Error($"I expected a Redis KV Key like: {errorFileNameKey} AND {errorFileContentKey} but they were not found, so no output file will be generated.");
                    }

                    
                    ITransaction redisTransaction = _redisPipelineManager.Cache.CreateTransaction();
                    redisTransaction =
                        _redisPipelineManager.Delete(redisTransaction, $"{fileToMove.TransactionUid}:File");
                    redisTransaction =
                        _redisPipelineManager.Delete(redisTransaction, $"{fileToMove.TransactionUid}:Filename");
                    log.Info($"Deleted Key {fileToMove.TransactionUid} for file {fileToMove.Name} in Redis");
                    redisTransaction = _redisPipelineManager.AcknowledgeError(redisTransaction, message);
                    redisTransaction.ExecuteAsync();
                }
                catch (SftpPathNotFoundException)
                {
                    log.Fatal("If an SFTP directory is incorrectly configured, the process cannot continue. Check if you forgot a trailing forwardslash!");
                    throw;
                }
                catch (Exception exception)
                {
                    log.Debug("An exception occured while processing a REDIS ERROR message received event (to move a file to ERROR), skipping for now", exception);

                    log.Error("An exception occured while processing a REDIS ERROR message received event (to move a file to ERROR), skipping for now. (SEE DEBUG LOG FOR DETAILS)");
                    log.Error($"This means that the REDIS Message with values: Redis-MessageId {message.RedisMessageId} TUID: {message.TransactionID} Content: {message.Content} will not be processed, but will be retried");
                    log.Error(exception.Message);
                }
               
            }
            
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_sftpBackgroundWorker.IsBusy) _sftpBackgroundWorker.RunWorkerAsync();
                if (!_redisPipelineManager.DoneBackgroundWorker.IsBusy)
                    _redisPipelineManager.DoneBackgroundWorker.RunWorkerAsync();
                if (!_redisPipelineManager.ErrorBackgroundWorker.IsBusy)
                    _redisPipelineManager.ErrorBackgroundWorker.RunWorkerAsync();



                log.Info($"SftpHandler alive! @: {DateTimeOffset.Now}");
                await Task.Delay(Convert.ToInt32(_configuration["General:TaskDelaySeconds"])*1000, stoppingToken);

            }

            _sftpBackgroundWorker.CancelAsync();
            _redisPipelineManager.ErrorBackgroundWorker.CancelAsync();
            _redisPipelineManager.DoneBackgroundWorker.CancelAsync();
        }
    }
}

//                                            
//                                                      .__....._            _.....__,
//                                                        .": o :':         ;': o :".
//                                                        `. `-' .'.       .'. `-' .'
//                                                          `---'             `---'
//                                            
//                                                _...----...      ...   ...      ...----..._
//                                             .-'__..-""'----    `.  `"`  .'    ----'""-..__`-.
//                                            '.-'   _.--"""'       `-._.-'       '"""--._   `-.`
//                                            '  .-"'                  :                  `"-.  `
//                                              '   `.              _.'"'._              .'   `
//                                                    `.       ,.-'"       "' -.,       .'
//                                                      `.                           .'
//                                                        `-._                   _.- '
//                                                            `"'--...___...--'"`