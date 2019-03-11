using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace DistributedLock.AzureStorage
{
    internal class AzureBlobLease : Lease
    {
        // ctor needed for deserialization
        internal AzureBlobLease()
        {
        }

        internal AzureBlobLease(string key, CloudBlockBlob blob) : base(key)
        {
            Blob = blob;
        }

        internal AzureBlobLease(AzureBlobLease source)
            : base(source)
        {
            Offset = source.Offset;
            Blob = source.Blob;
        }

        internal AzureBlobLease(Lease source, CloudBlockBlob blob) : base(source)
        {
            Offset = source.Offset;
            Blob = blob;
        }

        // do not serialize
        [JsonIgnore] public CloudBlockBlob Blob { get; }

        public override async Task<bool> IsExpired()
        {
            await Blob.FetchAttributesAsync().ConfigureAwait(false); // Get the latest metadata
            var currentState = Blob.Properties.LeaseState;
            return currentState != LeaseState.Leased;
        }
    }
}