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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Newtonsoft.Json;

namespace DistributedLocks.AzureStorage
{
    /// <summary>
    ///     Distributed locks using azure storage account.
    /// </summary>
    public class AzureStorageDistributedLock : IAzureStorageDistributedLock
    {
        private readonly IDistributedLockContext _currentContext;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);

        private int _disposeSignaled;
        private AzureBlobLease _currentLease;

        private AzureStorageDistributedLock(AzureStorageDistributedLockOptions options)
        {
            Options = options;
            _currentContext = new AzureStorageDistributedLockContext(this);
        }

        /// <summary>
        ///     Options to configure the azure storage distributed locker
        /// </summary>
        public AzureStorageDistributedLockOptions Options { get; }

        public bool Disposed { get; set; }

        /// <inheritdoc cref="IDistributedLock" />
        public Task ExecuteAsync(Func<IDistributedLockContext, Task> action)
        {
            return ExecuteAsync(async context =>
            {
                await action.Invoke(_currentContext).ConfigureAwait(false);
                return true;
            });
        }

        /// <inheritdoc cref="IDistributedLock" />
        public async Task<T> ExecuteAsync<T>(Func<IDistributedLockContext, Task<T>> result)
        {
            try
            {
                // if the task is executed within the same process we can lock by code each operation avoiding internal http calls if 
                // there is a single competitor node
                await _semaphore.WaitAsync().ConfigureAwait(false);

                var lease = await CreateLeaseIfNotExistsAsync(Options.Key).ConfigureAwait(false);
                _currentLease = lease;
                var operationPerformed = false;
                var value = default(T);

                var attempts = 0;
                while (!operationPerformed && attempts <= Options.RetryTimes)
                {
                    try
                    {
                        attempts++;
                        var acquired = await AcquireLeaseAsync(lease).ConfigureAwait(false);
                        if (acquired)
                        {
                            operationPerformed = true;
                            value = await result.Invoke(_currentContext).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await ReleaseLeaseAsync(lease).ConfigureAwait(false);
                    }
                    
                    if (!operationPerformed)
                    {
                        await Task.Delay(Options.RetryWaitTime).ConfigureAwait(false);
                    }
                }

                return value;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <inheritdoc cref="IDisposable" />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeSignaled, 1) != 0)
                return;

            _semaphore?.Release();
            Disposed = true;
        }

        /// <inheritdoc cref="IDistributedLock" />
        public async Task ReleaseLockAsync()
        {
            try
            {
                await _semaphore.WaitAsync().ConfigureAwait(false);
                var lease = await GetLeaseAsync(Options.Key).ConfigureAwait(false);

                if (lease != null)
                {
                    await lease.Blob.DeleteIfExistsAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        ///     Create an AzureStorage Distributed Locker.
        /// </summary>
        /// <param name="key">key for the lock.</param>
        /// <param name="optionsBuilder">options to build the locker.</param>
        /// <returns></returns>
        public static IDistributedLock Create(string key, Action<AzureStorageDistributedLockOptions> optionsBuilder = null)
        {
            var options = new AzureStorageDistributedLockOptions(key);
            optionsBuilder?.Invoke(options);

            var locker = new AzureStorageDistributedLock(options);

            return locker;
        }

        /// <summary>
        ///     Create an AzureStorage Distributed Locker. 
        /// </summary>
        /// <param name="key">key for the lock.</param>
        /// <param name="optionsBuilder">options to build the locker.</param>
        /// <returns></returns>
        public static Task<IDistributedLock> CreateAsync(string key, Action<AzureStorageDistributedLockOptions> optionsBuilder = null)
        {
            // Async is not used anymore, but this method was kept to not break existing code.
            var result = Task.FromResult(Create(key, optionsBuilder));
            return result;
        }

        /// <summary>
        ///     Renew the lease, getting more time to get a work done.
        /// </summary>
        /// <param name="renewInterval">required time</param>
        /// <returns></returns>
        public async Task<bool> RenewLease(TimeSpan renewInterval)
        {
            var renewRequestOptions = new RequestConditions();

            var lease = _currentLease;

            if (lease == null)
                return false;

            try
            {
                await lease.Blob.GetBlobLeaseClient(lease.Token).RenewAsync(renewRequestOptions).ConfigureAwait(false);
            }
            catch (RequestFailedException)
            {
                return false;
            }

            return true;
        }

        private BlockBlobClient GetBlockBlobReference(string key)
        {
            var leaseBlob = new BlockBlobClient(Options.ConnectionString, Options.Container.ToLower(), key);
            return leaseBlob;
        }

        private async Task<AzureBlobLease> CreateLeaseIfNotExistsAsync(string key) // throws URISyntaxException, IOException, StorageException
        {
            AzureBlobLease returnLease;
            try
            {
                var leaseBlob = GetBlockBlobReference(key);
                returnLease = new AzureBlobLease(key, leaseBlob);
                var jsonLease = JsonConvert.SerializeObject(returnLease);

                if (await leaseBlob.ExistsAsync(CancellationToken.None)) return await GetLeaseAsync(key).ConfigureAwait(false);

                await SetBlobStateAsync(returnLease).ConfigureAwait(false);
            }
            catch (RequestFailedException se)
            {
                if (se.ErrorCode == BlobErrorCode.BlobAlreadyExists ||
                    se.ErrorCode == BlobErrorCode.LeaseIdMissing) // occurs when somebody else already has leased the blob
                {
                    returnLease = await GetLeaseAsync(key).ConfigureAwait(false);
                }
                else
                {
                    throw;
                }
            }

            return returnLease;
        }

        private async Task<AzureBlobLease> GetLeaseAsync(string key) // throws URISyntaxException, IOException, StorageException
        {
            AzureBlobLease result = null;
            var leaseBlob = GetBlockBlobReference(key);

            if (await leaseBlob.ExistsAsync().ConfigureAwait(false))
            {
                result = await DownloadLeaseAsync(leaseBlob).ConfigureAwait(false);
            }

            return result;
        }

        private static async Task<AzureBlobLease> DownloadLeaseAsync(BlockBlobClient blob) // throws StorageException, IOException
        {
            var jsonLease = await blob.DownloadAsync().ConfigureAwait(false);
            using (var stream = new StreamReader(jsonLease.Value.Content))
            {
                var rehydrated = (AzureBlobLease)JsonConvert.DeserializeObject(stream.ReadToEnd(), typeof(AzureBlobLease));
                var blobLease = new AzureBlobLease(rehydrated, blob);

                return blobLease;
            }
        }

        private async Task<bool> AcquireLeaseAsync(AzureBlobLease lease)
        {
            var leaseBlob = lease.Blob;
            var newLeaseId = Guid.NewGuid().ToString();
            var lockerKey = lease.Key;
            try
            {
                string newToken;
                var props = await leaseBlob.GetPropertiesAsync().ConfigureAwait(false);

                if (props.Value.LeaseState == LeaseState.Leased)
                {
                    if (string.IsNullOrEmpty(lease.Token))
                    {
                        return false;
                    }

                    var response = await leaseBlob.GetBlobLeaseClient(lease.Token).ChangeAsync(newLeaseId);
                    newToken = response.Value.LeaseId;
                }
                else
                {
                    try
                    {
                        var response = await leaseBlob.GetBlobLeaseClient(lease.Token).AcquireAsync(Options.LeaseDuration).ConfigureAwait(false);
                        newToken = response.Value.LeaseId;
                    }
                    catch (RequestFailedException se)
                        when (se != null
                              && se.ErrorCode == BlobErrorCode.LeaseAlreadyPresent)
                    {
                        return false;
                    }
                }

                lease.Token = newToken;
                lease.IncrementEpoch(); // Increment epoch each time lease is acquired or stolen by a new host

                await SetBlobStateAsync(lease).ConfigureAwait(false);
            }
            catch (RequestFailedException se)
            {
                throw HandleStorageException(lockerKey, se);
            }

            return true;
        }

        private async Task SetBlobStateAsync(AzureBlobLease lease, string leaseId = null)
        {
            // if lease id is not sent then use the current lease token
            if (leaseId == null) leaseId = lease.Token;

            using (var uploadFileStream = new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(lease) ?? "")))
            {
                var options = new BlobUploadOptions()
                {
                    Conditions = new BlobRequestConditions()
                    {
                        LeaseId = leaseId
                    }
                };

                await lease.Blob.UploadAsync(uploadFileStream, options).ConfigureAwait(false);
            }
        }

        private static Exception HandleStorageException(string key, RequestFailedException se)
        {
                if (se.ErrorCode == null ||
                    se.ErrorCode == BlobErrorCode.LeaseLost ||
                    se.ErrorCode == BlobErrorCode.LeaseIdMismatchWithLeaseOperation ||
                    se.ErrorCode == BlobErrorCode.LeaseIdMismatchWithBlobOperation)
                {
                    return new Exception(key, se);
                }

            return se;
        }

        private async Task<bool> ReleaseLeaseAsync(AzureBlobLease lease)
        {
            if (string.IsNullOrWhiteSpace(lease.Token))
                return false;

            var leaseBlob = lease.Blob;
            var key = lease.Key;

            try
            {
                var leaseId = lease.Token;
                var releasedCopy = new AzureBlobLease(lease)
                {
                    Token = string.Empty
                };

                await SetBlobStateAsync(releasedCopy, leaseId).ConfigureAwait(false);

                await leaseBlob.GetBlobLeaseClient(leaseId).ReleaseAsync().ConfigureAwait(false);
            }
            catch (RequestFailedException se)
            {
                throw HandleStorageException(key, se);
            }

            return true;
        }
    }
}
