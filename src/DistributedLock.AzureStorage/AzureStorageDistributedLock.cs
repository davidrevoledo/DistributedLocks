using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob.Protocol;
using Newtonsoft.Json;

namespace DistributedLock.AzureStorage
{
    public class AzureStorageDistributedLock : IDistributedLock
    {
        private readonly AzureStorageDistributedLockOptions _options;
        private CloudStorageAccount _cloudStorageAccount;
        private CloudBlobDirectory _consumerGroupDirectory;
        private CloudBlobContainer _eventHubContainer;
        private OperationContext _operationContext;

        private AzureStorageDistributedLock(AzureStorageDistributedLockOptions options)
        {
            _options = options;
        }

        public Task Lock(Func<Task> action)
        {
            return Lock(async () =>
            {
                await action.Invoke().ConfigureAwait(false);
                return true;
            });
        }

        public async Task<T> Lock<T>(Func<Task<T>> result)
        {
            var lease = await CreateLeaseIfNotExistsAsync(_options.Key).ConfigureAwait(false);
            var operationPerformed = false;
            var value = default(T);

            var attempts = 0;
            while (!operationPerformed && attempts <= _options.RetryTimes)
            {
                try
                {
                    attempts++;
                    var acquired = await AcquireLeaseAsync(lease).ConfigureAwait(false);

                    if (acquired)
                    {
                        operationPerformed = true;
                        value = await result.Invoke().ConfigureAwait(false);
                    }
                    // todo remove
                    else
                    {
                        Console.WriteLine("Waiting for releasing....");
                    }
                }
                finally
                {
                    await ReleaseLeaseAsync(lease).ConfigureAwait(false);
                }

                await Task.Delay(_options.RetryWaitTime).ConfigureAwait(false);
            }

            return value;
        }

        public static async Task<IDistributedLock> Create(string key, Action<AzureStorageDistributedLockOptions> optionsBuilder = null)
        {
            var options = new AzureStorageDistributedLockOptions(key);
            optionsBuilder?.Invoke(options);

            var locker = new AzureStorageDistributedLock(options);

            await locker.Init().ConfigureAwait(false);
            return locker;
        }

        private async Task Init()
        {
            _cloudStorageAccount = CloudStorageAccount.Parse(_options.ConnectionString);
            var storageClient = _cloudStorageAccount.CreateCloudBlobClient();

            _eventHubContainer = storageClient.GetContainerReference(_options.Container.ToLower());
            await _eventHubContainer.CreateIfNotExistsAsync().ConfigureAwait(false);

            _consumerGroupDirectory = _eventHubContainer.GetDirectoryReference(_options.Directory.ToLower());
            _operationContext = new OperationContext();
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

                if (await leaseBlob.ExistsAsync(CancellationToken.None)) return (AzureBlobLease) await GetLeaseAsync(key).ConfigureAwait(false);

                await leaseBlob.UploadTextAsync(
                    jsonLease,
                    null,
                    AccessCondition.GenerateIfNoneMatchCondition("*"),
                    null,
                    _operationContext).ConfigureAwait(false);
            }
            catch (StorageException se)
            {
                if (se.RequestInformation.ErrorCode == BlobErrorCodeStrings.BlobAlreadyExists ||
                    se.RequestInformation.ErrorCode == BlobErrorCodeStrings.LeaseIdMissing) // occurs when somebody else already has leased the blob
                    returnLease = (AzureBlobLease) await GetLeaseAsync(key).ConfigureAwait(false);
                else
                    throw;
            }

            return returnLease;
        }

        private async Task<Lease> GetLeaseAsync(string key) // throws URISyntaxException, IOException, StorageException
        {
            AzureBlobLease result = null;

            var leaseBlob = GetBlockBlobReference(key);

            if (await leaseBlob.ExistsAsync(null, _operationContext).ConfigureAwait(false))
                result = await DownloadLeaseAsync(leaseBlob).ConfigureAwait(false);

            return result;
        }

        private static async Task<AzureBlobLease> DownloadLeaseAsync(CloudBlockBlob blob) // throws StorageException, IOException
        {
            var jsonLease = await blob.DownloadTextAsync().ConfigureAwait(false);
            var rehydrated = (AzureBlobLease) JsonConvert.DeserializeObject(jsonLease, typeof(AzureBlobLease));
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
                await leaseBlob.FetchAttributesAsync(null, null, _operationContext).ConfigureAwait(false);

                if (leaseBlob.Properties.LeaseState == LeaseState.Leased)
                {
                    if (string.IsNullOrEmpty(lease.Token)) return false;

                    newToken = await leaseBlob.ChangeLeaseAsync(
                        newLeaseId,
                        AccessCondition.GenerateLeaseCondition(lease.Token),
                        null,
                        _operationContext).ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        newToken = await leaseBlob.AcquireLeaseAsync(
                                _options.LeaseDurationInSeconds,
                                newLeaseId,
                                null,
                                null,
                                _operationContext)
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
                    _operationContext).ConfigureAwait(false);
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
                    return new Exception(key, se);

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
                    _operationContext).ConfigureAwait(false);
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