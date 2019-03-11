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