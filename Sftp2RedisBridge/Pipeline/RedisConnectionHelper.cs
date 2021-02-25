using System;
using System.Reflection;
using log4net;
using StackExchange.Redis;

namespace Sftp2RedisBridge.Pipeline
{
    public class RedisConnectionHelper
    {
        private Lazy<ConnectionMultiplexer> _lazyConnection;
        private static readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
        private readonly string _connectionString;
        public RedisConnectionHelper(string connectionString)
        {
            _log.Debug("Redis ConnectionString: " + connectionString);
            this._connectionString = connectionString;
        }

        public bool Connect()
        {
            try
            {
                
                _lazyConnection = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(_connectionString));
                
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                return false;
            }
         
        }
        public ConnectionMultiplexer Connection => _lazyConnection.Value;
    }
}