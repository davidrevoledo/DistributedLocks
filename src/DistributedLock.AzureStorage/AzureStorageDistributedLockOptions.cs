//MIT License
//Copyright(c) 2018 David Revoledo

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.
// Project Lead - David Revoledo davidrevoledo@d-genix.com
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