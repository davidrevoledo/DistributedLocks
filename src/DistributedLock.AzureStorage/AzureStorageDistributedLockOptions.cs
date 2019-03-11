using System;

namespace DistributedLock.AzureStorage
{
    public class AzureStorageDistributedLockOptions
    {
        public AzureStorageDistributedLockOptions(string key)
        {
            Key = key;
        }

        /// <summary>
        ///     Azure storage connection string.
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        ///     Container where to save checkpoints "distributedlocks" is default.
        /// </summary>
        public string Container { get; set; } = "distributedlocks";

        /// <summary>
        ///     Directory where checkpoints will be created "nodes" is default.
        /// </summary>
        public string Directory { get; set; } = "nodes";

        /// <summary>
        ///     Lease duration in seconds, this value should be from 1 to 60. (30 seconds is the default)
        ///     If your work will use more than 60 seconds consider split it.
        /// </summary>
        public TimeSpan LeaseDurationInSeconds { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     Time to wait when the action cannot be executed because the lock is waiting for a node releasing,
        ///     default is 200 milliseconds.
        /// </summary>
        public TimeSpan RetryWaitTime { get; set; } = TimeSpan.FromMilliseconds(200);

        /// <summary>
        ///     Times to retry the action before stop trying.
        /// </summary>
        public int RetryTimes { get; set; } = 10;

        /// <summary>
        ///     Key to block some action.
        /// </summary>
        public string Key { get; }
    }
}