using System;
using System.Threading.Tasks;

namespace DistributedLock
{
    public interface IDistributedLock
    {
        Task Lock(Func<Task> action);

        Task<T> Lock<T>(Func<Task<T>> result);
    }
}