using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

// https://dejanstojanovic.net/aspnet/2018/may/using-idistributedcache-in-net-core-just-got-a-lot-easier/
namespace CodeBoss.Extensions
{
    public static class SerializationExtensions
    {
        public static byte[] ToByteArray(this object obj)
        {
            if (obj == null) return null;

            BinaryFormatter binaryFormatter = new BinaryFormatter();
            using (MemoryStream memoryStream = new MemoryStream())
            {
                binaryFormatter.Serialize(memoryStream, obj);
                return memoryStream.ToArray();
            }
        }
        public static T FromByteArray<T>(this byte[] byteArray) where T : class
        {
            if (byteArray == null) return default;

            BinaryFormatter binaryFormatter = new BinaryFormatter();
            using (MemoryStream memoryStream = new MemoryStream(byteArray))
            {
                return binaryFormatter.Deserialize(memoryStream) as T;
            }
        }

    }
}
