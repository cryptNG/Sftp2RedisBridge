namespace Sftp2RedisBridge.Networking
{
    public class RedisMessage
    {
        public RedisMessage(string transactionId, string content, string redisMessageId)
        {
            TransactionID = transactionId;
            Content = content;
            RedisMessageId = redisMessageId;
        }

        public RedisMessage()
        {
        }

        public string TransactionID { get; set; }
        public string Content { get; set; }
        public string RedisMessageId { get; set; }
    }
}