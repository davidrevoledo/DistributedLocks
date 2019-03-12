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
using System.Threading.Tasks;

namespace DistributedLocks.AzureStorage
{
    /// <summary>
    ///     Context that represent the context of distributed locks using azure storage.
    /// </summary>
    public class AzureStorageDistributedLockContext : IDistributedLockContext
    {
        private readonly AzureStorageDistributedLock _locker;

        internal AzureStorageDistributedLockContext(AzureStorageDistributedLock locker)
        {
            _locker = locker;
        }

        /// <inheritdoc cref="IDistributedLockContext" />
        public Task<bool> RenewLeaseAsync(TimeSpan renewInterval)
        {
            if (renewInterval >= _locker.Options.LeaseDuration)
            {
                throw new ArgumentException("Renew interval needs to be smaller than the lease duration.");
            }

            return _locker.RenewLease(renewInterval);
        }
    }
}