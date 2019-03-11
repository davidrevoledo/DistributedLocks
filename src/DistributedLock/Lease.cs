using System.Threading.Tasks;

namespace DistributedLock
{
    /// <summary>
    ///     Contains locker ownership information.
    /// </summary>
    public class Lease
    {
        /// <summary></summary>
        protected Lease()
        {
        }

        /// <summary></summary>
        /// <param name="key"></param>
        protected Lease(string key)
        {
            Key = key;
        }

        /// <summary></summary>
        /// <param name="source"></param>
        protected Lease(Lease source)
        {
            Key = source.Key;
            Epoch = source.Epoch;
        }

        /// <summary>
        ///     Gets or sets the current value for the offset in the stream.
        /// </summary>
        public string Offset { get; set; }

        /// <summary>
        ///     Gets the ID of the partition to which this lease belongs.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        ///     Gets or sets the epoch year of the lease, which is a value you can use to determine the most recent owner of a
        ///     partition between competing nodes.
        /// </summary>
        public long Epoch { get; set; }

        /// <summary>
        ///     Gets or sets the lease token that manages concurrency between hosts. You can use this token to guarantee single
        ///     access to any resource needed.
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        ///     Determines whether the lease is expired.
        /// </summary>
        /// <returns></returns>
        public virtual Task<bool> IsExpired()
        {
            // By default lease never expires.
            // Deriving class will implement the lease expiry logic.
            return Task.FromResult(false);
        }

        public long IncrementEpoch()
        {
            Epoch++;
            return Epoch;
        }
    }
}