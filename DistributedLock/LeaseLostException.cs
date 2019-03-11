using System;

namespace DistributedLock
{
    /// <summary>
    ///     Represents an exception that occurs when the service lease has been lost.
    /// </summary>
    public class LeaseLostException : Exception
    {
        public LeaseLostException(string key, Exception innerException)
            : base(innerException.Message, innerException)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            Key = key;
        }

        /// <summary>
        ///     Gets the lease key where the exception occured.
        /// </summary>
        public string Key { get; }
    }
}