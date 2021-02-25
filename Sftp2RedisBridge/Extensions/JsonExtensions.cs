using System.Text.Json;

namespace Sftp2RedisBridge.Extensions
{
    public static class JsonExtensions
    {
        /// <summary>
        ///     The string representation of null.
        /// </summary>
        private static readonly string Null = "null";


        /// <summary>
        ///     To json.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The Json of any object.</returns>
        public static string ToJson(this object value)
        {
            if (value == null) return Null;


            var json = JsonSerializer.Serialize(value);
            return json;
        }

        public static T FromJson<T>(this string value)
        {
            var result = JsonSerializer.Deserialize<T>(value);


            return result;
        }
    }
}