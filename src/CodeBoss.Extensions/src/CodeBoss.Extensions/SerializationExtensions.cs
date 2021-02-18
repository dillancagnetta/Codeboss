using System;
using System.Text.Json;

// https://dejanstojanovic.net/aspnet/2018/may/using-idistributedcache-in-net-core-just-got-a-lot-easier/
namespace CodeBoss.Extensions
{
    public static class SerializationExtensions
    {
        public static byte[] ToByteArray(this object obj)
        {
            if (obj == null) return null;

            return JsonSerializer.SerializeToUtf8Bytes(obj);
        }
        public static T FromByteArray<T>(this byte[] byteArray) where T : class
        {
            if (byteArray == null) return default;

            var readOnlySpan = new ReadOnlySpan<byte>(byteArray);
            return JsonSerializer.Deserialize<T>(readOnlySpan);
        }

    }
}
