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

namespace DistributedLocks
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