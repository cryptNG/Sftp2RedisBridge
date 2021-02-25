using System;
using System.Collections.Generic;
using System.ComponentModel;
using StackExchange.Redis;
using Sftp2RedisBridge.Networking;
using Sftp2RedisBridge.Pipeline;

namespace Sftp2RedisBridge.Contracts
{
    public interface IRedisPipelineManager : IDisposable
    {
        IDatabase Cache { get; }
        BackgroundWorker DoneBackgroundWorker { get; set; }
        bool WriteToErrorStream(string tuid, string message);
        bool WriteToDoneStream(string tuid, string message);
        bool WriteToProcessStream(string tuid);
        BackgroundWorker ErrorBackgroundWorker { get; set; }
        bool LocalCacheContains(string key);
        bool SetOrUpdateCachedFiles(List<RemoteFile> filesToUpdate);
        List<RemoteFile> GetCachedFiles();
        bool ClearCachedFiles();
        ITransaction DeleteCachedFile(ITransaction transaction, string transactionUid);
        bool DeleteCachedFile(RemoteFile memoryFile);
        bool Initialize();
        bool DeleteCachedFile(string transactionUid);
        ITransaction AcknowledgeDone(ITransaction transaction, RedisMessage message);
        ITransaction AcknowledgeError(ITransaction transaction, RedisMessage message);
        ITransaction Delete(ITransaction transaction, string Key);

        bool Delete(string Key);
        string Get(string Key);
        bool Contains(string key);
        void RegisterDoneActionHandler(RedisDoneAction callBack);
        void RegisterErrorActionHandler(RedisErrorAction callBack);
        bool Set(string Key, string Value);
    }
}