using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using log4net;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using Sftp2RedisBridge.Contracts;
using Sftp2RedisBridge.Extensions;
using Sftp2RedisBridge.Networking;

namespace Sftp2RedisBridge.Pipeline
{
    public delegate void RedisDoneAction(string guid);

    public delegate void RedisErrorAction(string guid);

    public class RedisPipelineManager : IRedisPipelineManager
    {
     
        private string _doneStreamName = "done";
        private string _errorStreamName = "error";
        private string _processStreamName = "process";
        private readonly int _keyValueStoreExpiry;
        private const string RemoteFilesCache = "RemoteFilesCache";
        private const string ConsumerGroupName = "sftpConsumerGroup";
        private const string ConsumerName = "sftp2RedisService";
        private const int MinimumMessageReceiveCount = 100;
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
        private readonly IConfiguration _configuration;
        private readonly RedisConnectionHelper _connectionHelper;
        private readonly int _expiryTimeMilliSeconds;
        private readonly int _waitTimeBeforeProcessPendingInMilliseconds;
        private readonly int _pollingIdleTimeMilliSeconds;

        private RedisDoneAction _doneCallbackHandler;
        private RedisErrorAction _errorCallbackHandler;


        public RedisPipelineManager(IConfiguration configuration)
        {
            //THREAD THEFT?  https://stackexchange.github.io/StackExchange.Redis/ThreadTheft.html
            DoneBackgroundWorker = new BackgroundWorker();
            ErrorBackgroundWorker = new BackgroundWorker();
            DoneBackgroundWorker.DoWork += ListenDoneStream;
            ErrorBackgroundWorker.DoWork += ListenErrorStream;
            DoneBackgroundWorker.WorkerSupportsCancellation = true;
            ErrorBackgroundWorker.WorkerSupportsCancellation = true;
            _configuration = configuration;
            _connectionHelper = new RedisConnectionHelper(configuration["Redis:ConnectionString"]);
            _doneStreamName = configuration["Redis:DoneStreamName"]??"done";
            _errorStreamName = configuration["Redis:ErrorStreamName"]??"error";
            _keyValueStoreExpiry = Convert.ToInt32(_configuration["Redis:KeyValueStoreExpirySeconds"]) * 1000;
            _expiryTimeMilliSeconds = Convert.ToInt32(configuration["Redis:StreamExpiryTimeInSeconds"]) * 1000;
            _waitTimeBeforeProcessPendingInMilliseconds = Convert.ToInt32(configuration["Redis:WaitToProcessPendingInSeconds"]??"10") * 1000;
            _pollingIdleTimeMilliSeconds = Convert.ToInt32(configuration["Redis:PollingIdleTimeSeconds"]) * 1000;

            //  someNullableObject?.SomeProperty??thisValueIfAllNull  (TEST ON INT)

          

            if (_keyValueStoreExpiry == 0)
            {
                _keyValueStoreExpiry = 3600 * 1000;//1h default
            }

            
            if (_expiryTimeMilliSeconds == 0)
            {
                _expiryTimeMilliSeconds = 30 * 1000; // 30 sek default
            }
            if (_pollingIdleTimeMilliSeconds == 0)
            {
                _pollingIdleTimeMilliSeconds = 30 * 1000; // 30 sek default
            }

            log.Debug("StreamExpiryTime in MS: " + _expiryTimeMilliSeconds);
            log.Debug("KeyValueStoreExpiryTime in MS: " + _keyValueStoreExpiry);

            log.Debug("Polling Time in MS: " + _pollingIdleTimeMilliSeconds);

          
        
        }

        public bool Initialize()
        {
            try
            {
                //Only connect and try to recreate ConsumerGroup if has been disconnected or first connect
                //handling not initialize _lazyConnection reference. Though IsConnected should work always
                if (Cache?.IsConnected("any")??false)
                {
                    return true;
                }

                if (_connectionHelper.Connect())
                {
                    if (Cache.IsConnected("any"))
                    {
                        log.Debug("Connected to redis");
                        try
                        {
                            try
                            {
                                Cache.StreamCreateConsumerGroup(_doneStreamName, ConsumerGroupName, "$");
                                log.Info(
                                    $"Stream {_doneStreamName} for ConsumerGroup {ConsumerGroupName} does not exist, creating.");
                            }
                            catch 
                            {
                                //ignored
                            }




                            try
                            {
                                Cache.StreamCreateConsumerGroup(_errorStreamName, ConsumerGroupName, "$");
                                log.Info(
                                    $"Stream {_errorStreamName} ConsumerGroup {ConsumerGroupName} does not exist, creating.");
                            }
                            catch
                            {
                                // ignored
                            }

                            return true;
                        }
                        catch (Exception ex)
                        {
                            log.Error("An exception occurred", ex);
                        }
                    }
                    
                }
               
               
            }
            catch (Exception ex)
            {
                log.Error("CONNECTION FAILED", ex);
                log.Error(
                    "Check following parameters: [RedisServer available] [Configuration valid (if you use environment variables, do not use '' ] [Port valid] if on docker, try using an IP address instead of a service name");
            }

            return false;
        }


        public BackgroundWorker DoneBackgroundWorker { get; set; }
        public BackgroundWorker ErrorBackgroundWorker { get; set; }

        public bool WriteToDoneStream(string tuid, string message)
        {
            try
            {
                Cache.StreamAdd(_doneStreamName, tuid, message);
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return false;
            }
            return true;
        }
        public bool WriteToProcessStream(string tuid)
        {
            try
            {
                Cache.StreamAdd(_processStreamName, tuid, "");
            }
            catch (Exception ex)
            {
               log.Error(ex);
               return false;
            }
            return true;
        }
        public bool WriteToErrorStream(string tuid, string message)
        {
            try
            {
                Cache.StreamAdd(_errorStreamName, tuid, message);
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return false;
            }
            return true;
        }

        public bool AcknowledgeDone(RedisMessage message)
        {
            try
            {
                log.Debug("AcknowledgeDone/DeleteKey from Redis for " + message.TransactionID);
                Cache.StreamAcknowledge(_doneStreamName, ConsumerGroupName, message.RedisMessageId);
                Cache.StreamDelete(_doneStreamName, new RedisValue[] { message.RedisMessageId });
                DeleteCachedFile(message.TransactionID);
                log.Debug("Deleted Cache for " + message.TransactionID);
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return false;
            }

            return true;
        }

        public bool AcknowledgeError(RedisMessage message)
        {
            try
            {

                log.Debug("AcknowledgeError/DeleteKey from Redis for " + message.TransactionID);
                Cache.StreamAcknowledge(_errorStreamName, ConsumerGroupName, message.RedisMessageId);
                Cache.StreamDelete(_errorStreamName, new RedisValue[] { message.RedisMessageId });
                DeleteCachedFile(message.TransactionID);
                log.Debug("Deleted Cache for " + message.TransactionID);
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return false;
            }

            return true;
        }

        public ITransaction AcknowledgeDone(ITransaction transaction,RedisMessage message)
        {
            log.Debug("AcknowledgeDone/DeleteKey from Redis for " + message.TransactionID);

            transaction.StreamAcknowledgeAsync(_doneStreamName, ConsumerGroupName, message.RedisMessageId);
            transaction.StreamDeleteAsync(_doneStreamName, new RedisValue[] { message.RedisMessageId });
            return DeleteCachedFile(transaction,message.TransactionID);

        }

        public ITransaction AcknowledgeError(ITransaction transaction,RedisMessage message)
        {
            log.Debug("[TRANSACTION] AcknowledgeError/DeleteKey from Redis for " + message.TransactionID);
            transaction.StreamAcknowledgeAsync(_errorStreamName, ConsumerGroupName, message.RedisMessageId);
            transaction.StreamDeleteAsync(_errorStreamName, new RedisValue[] { message.RedisMessageId });
            return DeleteCachedFile(transaction,message.TransactionID);
        }

        public ITransaction Delete(ITransaction transaction, string Key)
        {
            transaction.KeyDeleteAsync(Key);
            return transaction;
        }

        public List<RemoteFile> GetCachedFiles()
        {
            if (Cache.KeyExists(RemoteFilesCache))
            {
                var jsonRemoteFileList = Get(RemoteFilesCache);
                var cachedObjects = jsonRemoteFileList.FromJson<List<RemoteFile>>();
                return cachedObjects;
            }

            return new List<RemoteFile>();
        }

        public bool ClearCachedFiles()
        {
            try
            {
                Cache.KeyDelete(RemoteFilesCache);
                return true;
            }
            catch (Exception ex)
            {
                log.Error("An exception occurred while trying to execute CLEARCACHEDFILES", ex);
                return false;
            }
        }

        public bool DeleteCachedFile(RemoteFile memoryFile)
        {
            try
            {
                var jsonRemoteFileList = Get(RemoteFilesCache);
                var cachedObjects = jsonRemoteFileList.FromJson<List<RemoteFile>>();
                var objectToRemove = cachedObjects.First(x =>
                    x.TransactionUid == memoryFile.TransactionUid && x.Name == memoryFile.Name);
                cachedObjects.Remove(objectToRemove);
                var redisTransaction = Cache.CreateTransaction();
                redisTransaction.KeyDeleteAsync(RemoteFilesCache);
                redisTransaction.StringSetAsync(RemoteFilesCache, cachedObjects.ToJson());
                redisTransaction.ExecuteAsync();
                return true;
            }
            catch (Exception ex)
            {
                log.Error("An exception occurred while trying to execute DELETECACHEDFILE (by remoteFile)", ex);
                return false;
            }
        }

        public ITransaction DeleteCachedFile(ITransaction transaction,string transactionUid)
        {

                var jsonRemoteFileList = Get(RemoteFilesCache);
                var cachedObjects = jsonRemoteFileList.FromJson<List<RemoteFile>>();
                var objectToRemove = cachedObjects.First(x => x.TransactionUid == transactionUid);
                cachedObjects.Remove(objectToRemove);

                transaction.KeyDeleteAsync(RemoteFilesCache);
                transaction.StringSetAsync(RemoteFilesCache, cachedObjects.ToJson());
                return transaction;
          
        }

        public bool DeleteCachedFile(string transactionUid)
        {
            try
            {
                var jsonRemoteFileList = Get(RemoteFilesCache);
                var cachedObjects = jsonRemoteFileList.FromJson<List<RemoteFile>>();
                var objectToRemove = cachedObjects.First(x => x.TransactionUid == transactionUid);
                cachedObjects.Remove(objectToRemove);
                var redisTransaction = Cache.CreateTransaction();
                redisTransaction.KeyDeleteAsync(RemoteFilesCache);
                redisTransaction.StringSetAsync(RemoteFilesCache, cachedObjects.ToJson());
                redisTransaction.ExecuteAsync();
                return true;
            }
            catch (Exception ex)
            {
                log.Error("An exception occurred while trying to execute DELETECACHEDFILE (by transactionId)", ex);
                return false;
            }
        }

        public bool SetOrUpdateCachedFiles(List<RemoteFile> memoryObjects)
        {
            try
            {
                if (!Cache.KeyExists(RemoteFilesCache))
                {
                    log.Debug("RemoteFilesCache key does not exist yet, creating.");
                    foreach (var file in memoryObjects)
                        log.Debug("Adding FILE: " + file.Name + "TUID: " + file.TransactionUid + " to Cache");
                    Cache.StringSet(RemoteFilesCache, memoryObjects.ToJson());
                    return true;
                }

                var jsonRemoteFileList = Get(RemoteFilesCache);
                var cachedObjects = jsonRemoteFileList.FromJson<List<RemoteFile>>();

                //  List<RemoteFile> updatedObjectsToCache= memoryObjects.Except(cachedObjects).ToList();            //TODO: CHECK LOGICS HERE TWICE
                foreach (var file in memoryObjects)
                {
                    var logMessage = $"Remote contains FILE: {file.FullName} with TUID: {file.TransactionUid} ";
                    if (!cachedObjects.Contains(file))
                    {
                        logMessage += "but the file is not yet cached, adding to cache.";
                        cachedObjects.Add(file);
                    }

                    log.Debug(logMessage);
                }

                List<RemoteFile> validFiles = new List<RemoteFile>();
                foreach (RemoteFile cachedFile in cachedObjects)
                {
                    if (memoryObjects.Contains(cachedFile))
                    {

                        validFiles.Add(cachedFile);
                    }
                    else
                    {
                        log.Info("The server does not have file: " + cachedFile.FullName + " deleting from cache.");
                    }

                }

                try
                {
                    var redisTransaction = Cache.CreateTransaction();
                    redisTransaction.KeyDeleteAsync(RemoteFilesCache);
                    redisTransaction.StringSetAsync(RemoteFilesCache, validFiles.ToJson());
                    redisTransaction.ExecuteAsync();
                }
                catch (Exception ex)
                {
                    log.Error(
                        "An exception occurred while trying to execute a RedisTransaction to remote the RemoteFilesCache",
                        ex);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                log.Error("An exception occurred while trying to execute SETORUPDATECACHEDFILES", ex);
                return false;
            }
        }

        public bool Contains(string key)
        {
            try
            {
                return Cache.KeyExists(key);
            }
            catch (Exception ex)
            {
                log.Error("An exception occurred while trying to execute CONTAINS on Redis KDB", ex);
                throw;
            }
        }
        public bool LocalCacheContains(string key)
        {
            try
            {
                return GetCachedFiles().Any(x => x.TransactionUid == key);
            }
            catch (Exception ex)
            {
                log.Error("An exception occurred while trying to execute CONTAINS on Redis KDB", ex);
                throw;
            }
        }

        public IDatabase Cache => _connectionHelper.Connection?.GetDatabase();

        public string Get(string Key)
        {
            try
            {
                return Cache.StringGet(Key);
            }
            catch (Exception ex)
            {
                log.Error("An exception occurred while trying to execute GET on Redis KBD", ex);
                throw;
            }
        }

        public void RegisterDoneActionHandler(RedisDoneAction callBack)
        {
            _doneCallbackHandler = callBack;
        }


        public void RegisterErrorActionHandler(RedisErrorAction callBack)
        {
            _errorCallbackHandler = callBack;
        }


        public bool Set(string Key, string Value)
        {
            try
            {
                TimeSpan expiryTimeSpan =
                    new TimeSpan(0, 0, _keyValueStoreExpiry);

                Cache.StringSet(Key, Value,
                    expiryTimeSpan, 0);
                return true;
            }

            catch (Exception ex)
            {
                log.Error("An exception occurred while trying to execute SET on Redis KDB", ex);
                throw;
            }
        }
    
        public bool Delete(string Key)
        {
            try
            {
                Cache.KeyDelete(Key);
                return true;
            }
            catch (Exception ex)
            {
                log.Error("An exception occurred while trying to execute CLEARCACHEDFILES", ex);
                throw;
            }
        }

        public void ListenDoneStream(object sender, DoWorkEventArgs e)
        {
            StreamEntry[] currentMessages = Array.Empty<StreamEntry>(); 
            do
            {
                try
                {
                    if (DoneBackgroundWorker.CancellationPending)
                    {
                        e.Cancel = true;
                        return;
                    }

                    log.Debug("Listen DoneStream Idle: " + _pollingIdleTimeMilliSeconds);
                    Thread.Sleep(_pollingIdleTimeMilliSeconds);

                    Initialize();
                    

                    if (DoneBackgroundWorker.CancellationPending)
                    {
                        e.Cancel = true;
                        return;
                    }

                    log.Debug("Looking for pending messages in " + _doneStreamName);
                    var pending = Cache.StreamPendingMessages(_doneStreamName, ConsumerGroupName, MinimumMessageReceiveCount,
                        ConsumerName);
                    if (pending != null && pending.Length > 0)
                    {
                        log.Debug($"Found {pending.Count()} pending messages");
                        //IDLE TIME > THREAD WAIT TIME
                        var pendingOrphaned = pending.Where(x => x.IdleTimeInMilliseconds > _waitTimeBeforeProcessPendingInMilliseconds);
                        if (pendingOrphaned.Any())
                        {
                            foreach (var inf in pendingOrphaned)
                            {
                                log.Debug("Found orphaned pending messages in doneStream.");
                                log.Debug(
                                    $"Claiming MESSAGE [MessageId:{inf.MessageId}] [IdleTime:{inf.IdleTimeInMilliseconds}(ms)] [ConsumerName:{inf.ConsumerName}] ");
                            }

                            RedisValue[] orphanedIds = pendingOrphaned.Select(x => x.MessageId).ToArray();
                            currentMessages = Cache.StreamClaim(_doneStreamName, ConsumerGroupName, ConsumerName,
                                _expiryTimeMilliSeconds, orphanedIds);
                        }
                    }

                    if (currentMessages == null || currentMessages.Length == 0)
                        currentMessages = Cache.StreamReadGroup(_doneStreamName, ConsumerGroupName, ConsumerName);


                    //Why this. Just get them in next loop
                    //if (currentMessages.Any())
                    //{
                    //    log.Debug("CurrentMessages.Length: " + currentMessages.Length);
                    //    var additionalUnclaimedEntries =
                    //        Cache.StreamReadGroup(_doneStreamName, ConsumerGroupName, ConsumerName);
                    //    log.Debug($"Found {additionalUnclaimedEntries.Length} additional entries to claim");
                    //    foreach (var entry in additionalUnclaimedEntries)
                    //        log.Debug(
                    //            $"Adding EntryId: {entry.Id} Key: {entry.Values[0].Name} Value: {entry.Values[0].Value}");

                    //    var bro = currentMessages.Concat(additionalUnclaimedEntries);
                    //    _ = currentMessages.Concat(additionalUnclaimedEntries); //TODO: Check
                    //    log.Debug("CurrentMessages.Length: " + currentMessages.Length);
                    //}
                }
                catch (Exception exception)
                {
                    log.Error(exception);
                }
                
            } while (currentMessages.Length <= 0);

            //There is no need to reclaim pending messages or claim messages from current StreamReadGroup request
            //RedisValue[] idsForCurrentMessages = currentMessages.Select(x => x.Id).ToArray();

            //Cache.StreamClaim(_doneStreamName, ConsumerGroupName, ConsumerName, _expiryTimeMilliSeconds,
            //    idsForCurrentMessages);

            //Do not create work if cancelation is pending
            if (DoneBackgroundWorker.CancellationPending)
            {
                e.Cancel = true;
                return;
            }

            var messages = new List<RedisMessage>();
            //Does nothing
            //log.Debug("Listing all current workingMessages:");
            //foreach (var mess in messages)
            //    log.Debug(
            //        $"(REDIS)ID: {mess.RedisMessageId}  TUID(REDISKEY): {mess.TransactionID} VALUE: {mess.Content}");
            log.Debug("Listing all added workingMessages:");

            
            
            foreach (var message in currentMessages)
            {
                log.Debug(
                    $"(REDIS)ID: {message.Id}  TUID(REDISKEY): {message.Values[0].Name} VALUE: {message.Values[0].Value}");

                messages.Add(new RedisMessage(message.Values[0].Name, message.Values[0].Value, message.Id));
            }
           

            e.Result = messages;
        }

        public void ListenErrorStream(object sender, DoWorkEventArgs e)
        {
            StreamEntry[] currentMessages = Array.Empty<StreamEntry>();
            do
            {
                try
                {
                    //Check always before sleep and after longer tasks
                    if (DoneBackgroundWorker.CancellationPending)
                    {
                        e.Cancel = true;
                        return;
                    }

                    //Sleep before initialze to connection sort itself out 
                    log.Debug("Listen ErrorStream Idle: " + _pollingIdleTimeMilliSeconds);
                    Thread.Sleep(_pollingIdleTimeMilliSeconds);

                    
                    if (DoneBackgroundWorker.CancellationPending)
                    {
                        e.Cancel = true;
                        return;

                    }
                    Initialize();
                    
                    

                    log.Debug("Looking for pending messages in " + _errorStreamName);
                    StreamPendingMessageInfo[] pending = null;
                    try
                    {
                        pending = Cache.StreamPendingMessages(_errorStreamName, ConsumerGroupName, MinimumMessageReceiveCount,
                            ConsumerName);
                    }
                    catch (RedisTimeoutException rTimeOutEx)
                    {
                        log.Error(rTimeOutEx);
                        throw;
                    }

                    StreamPendingMessageInfo[] pendingOrphaned = null;

                    if (pending.Length > 0)
                        pendingOrphaned = pending.Where(x => x.IdleTimeInMilliseconds > _expiryTimeMilliSeconds).ToArray();

                    if (pendingOrphaned != null && pendingOrphaned.Length > 0)
                    {
                        log.Debug($"Found {pending.Count()} pending messages");
                        foreach (var inf in pending)
                        {
                            log.Debug("Found orphaned pending messages in errorStream.");
                            log.Debug(
                                $"Claiming MESSAGE [MessageId:{inf.MessageId}] [IdleTime:{inf.IdleTimeInMilliseconds}(ms)] [ConsumerName:{inf.ConsumerName}] ");
                        }

                        currentMessages = Cache.StreamClaim(_errorStreamName, ConsumerGroupName, ConsumerName,
                            _expiryTimeMilliSeconds, pendingOrphaned.Select(x => x.MessageId).ToArray());
                    }

                    if (currentMessages == null || currentMessages.Length == 0)
                        currentMessages = Cache.StreamReadGroup(_errorStreamName, ConsumerGroupName, ConsumerName);


                    //Just consume in next loop
                    //if (currentMessages.Any())
                    //{
                    //    log.Debug("CurrentMessages.Length: " + currentMessages.Length);
                    //    var additionalUnclaimedEntries =
                    //        Cache.StreamReadGroup(_errorStreamName, ConsumerGroupName, ConsumerName);

                    //    _ = currentMessages.Concat(additionalUnclaimedEntries);
                    //    log.Debug("CurrentMessages.Length: " + currentMessages.Length);
                    //}
                }
                catch (Exception exception)
                {
                    log.Error(exception);
                }

                
            } while (currentMessages.Length <= 0);

            //No need to reclaom
            //RedisValue[] idsForCurrentMessages = currentMessages.Select(x => x.Id).ToArray();
            //Cache.StreamClaim(_errorStreamName, ConsumerGroupName, ConsumerName, _expiryTimeMilliSeconds,
            //    idsForCurrentMessages);

            //Do not create work if cancelation pending
            if(ErrorBackgroundWorker.CancellationPending)
            {
                e.Cancel=true;
                return;
            }
            var messages = new List<RedisMessage>();

            foreach (var message in currentMessages)
                messages.Add(new RedisMessage(message.Values[0].Name, message.Values[0].Value, message.Id));
            e.Result = messages;
        }

        public void Dispose()
        {
            DoneBackgroundWorker?.Dispose();
            ErrorBackgroundWorker?.Dispose();
        }
    }
}