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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.Blob.Protocol;
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

        private CloudStorageAccount _cloudStorageAccount;
        private CloudBlobDirectory _consumerGroupDirectory;
        private int _disposeSignaled;
        private CloudBlobContainer _eventHubContainer;
        private OperationContext _storageContext;
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

                    await Task.Delay(Options.RetryWaitTime).ConfigureAwait(false);
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
        public static async Task<IDistributedLock> CreateAsync(string key, Action<AzureStorageDistributedLockOptions> optionsBuilder = null)
        {
            var options = new AzureStorageDistributedLockOptions(key);
            optionsBuilder?.Invoke(options);

            var locker = new AzureStorageDistributedLock(options);

            await locker.Init().ConfigureAwait(false);
            return locker;
        }

        /// <summary>
        ///     Renew the lease, getting more time to get a work done.
        /// </summary>
        /// <param name="renewInterval">required time</param>
        /// <returns></returns>
        public async Task<bool> RenewLease(TimeSpan renewInterval)
        {
            var renewRequestOptions = new BlobRequestOptions
            {
                ServerTimeout = renewInterval,
                MaximumExecutionTime = Options.LeaseDuration
            };

            var lease = _currentLease;

            if (lease == null)
                return false;

            try
            {
                await lease.Blob.RenewLeaseAsync(
                    AccessCondition.GenerateLeaseCondition(lease.Token),
                    renewRequestOptions,
                    _storageContext).ConfigureAwait(false);
            }
            catch (StorageException)
            {
                return false;
            }

            return true;
        }

        private async Task Init()
        {
            _cloudStorageAccount = CloudStorageAccount.Parse(Options.ConnectionString);
            var storageClient = _cloudStorageAccount.CreateCloudBlobClient();

            _eventHubContainer = storageClient.GetContainerReference(Options.Container.ToLower());
            await _eventHubContainer.CreateIfNotExistsAsync().ConfigureAwait(false);

            _consumerGroupDirectory = _eventHubContainer.GetDirectoryReference(Options.Directory.ToLower());
            _storageContext = new OperationContext();
        }

        private CloudBlockBlob GetBlockBlobReference(string key)
        {
            var leaseBlob = _consumerGroupDirectory.GetBlockBlobReference(key);
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

                await leaseBlob.UploadTextAsync(
                    jsonLease,
                    null,
                    AccessCondition.GenerateIfNoneMatchCondition("*"),
                    null,
                    _storageContext).ConfigureAwait(false);
            }
            catch (StorageException se)
            {
                if (se.RequestInformation.ErrorCode == BlobErrorCodeStrings.BlobAlreadyExists ||
                    se.RequestInformation.ErrorCode == BlobErrorCodeStrings.LeaseIdMissing) // occurs when somebody else already has leased the blob
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

            if (await leaseBlob.ExistsAsync(null, _storageContext).ConfigureAwait(false))
            {
                result = await DownloadLeaseAsync(leaseBlob).ConfigureAwait(false);
            }

            return result;
        }

        private static async Task<AzureBlobLease> DownloadLeaseAsync(CloudBlockBlob blob) // throws StorageException, IOException
        {
            var jsonLease = await blob.DownloadTextAsync().ConfigureAwait(false);
            var rehydrated = (AzureBlobLease)JsonConvert.DeserializeObject(jsonLease, typeof(AzureBlobLease));
            var blobLease = new AzureBlobLease(rehydrated, blob);
            return blobLease;
        }

        private async Task<bool> AcquireLeaseAsync(AzureBlobLease lease)
        {
            var leaseBlob = lease.Blob;
            var newLeaseId = Guid.NewGuid().ToString();
            var lockerKey = lease.Key;
            try
            {
                string newToken;
                await leaseBlob.FetchAttributesAsync(null, null, _storageContext).ConfigureAwait(false);

                if (leaseBlob.Properties.LeaseState == LeaseState.Leased)
                {
                    if (string.IsNullOrEmpty(lease.Token))
                    {
                        return false;
                    }

                    newToken = await leaseBlob.ChangeLeaseAsync(
                        newLeaseId,
                        AccessCondition.GenerateLeaseCondition(lease.Token),
                        null,
                        _storageContext).ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        newToken = await leaseBlob.AcquireLeaseAsync(
                                Options.LeaseDuration,
                                newLeaseId,
                                null,
                                null,
                                _storageContext)
                            .ConfigureAwait(false);
                    }
                    catch (StorageException se)
                        when (se.RequestInformation != null
                              && se.RequestInformation.ErrorCode.Equals(BlobErrorCodeStrings.LeaseAlreadyPresent, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                lease.Token = newToken;
                lease.IncrementEpoch(); // Increment epoch each time lease is acquired or stolen by a new host
                await leaseBlob.UploadTextAsync(
                    JsonConvert.SerializeObject(lease),
                    null,
                    AccessCondition.GenerateLeaseCondition(lease.Token),
                    null,
                    _storageContext).ConfigureAwait(false);
            }
            catch (StorageException se)
            {
                throw HandleStorageException(lockerKey, se);
            }

            return true;
        }

        private static Exception HandleStorageException(string key, StorageException se)
        {
            if (se.RequestInformation.HttpStatusCode == 409 || // conflict
                se.RequestInformation.HttpStatusCode == 412) // precondition failed
                if (se.RequestInformation.ErrorCode == null ||
                    se.RequestInformation.ErrorCode == BlobErrorCodeStrings.LeaseLost ||
                    se.RequestInformation.ErrorCode == BlobErrorCodeStrings.LeaseIdMismatchWithLeaseOperation ||
                    se.RequestInformation.ErrorCode == BlobErrorCodeStrings.LeaseIdMismatchWithBlobOperation)
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
                await leaseBlob.UploadTextAsync(
                    JsonConvert.SerializeObject(releasedCopy),
                    null,
                    AccessCondition.GenerateLeaseCondition(leaseId),
                    null,
                    _storageContext).ConfigureAwait(false);
                await leaseBlob.ReleaseLeaseAsync(AccessCondition.GenerateLeaseCondition(leaseId)).ConfigureAwait(false);
            }
            catch (StorageException se)
            {
                throw HandleStorageException(key, se);
            }

            return true;
        }
    }
}