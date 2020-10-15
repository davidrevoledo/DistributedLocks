using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;
using DistributedLocks;
using DistributedLocks.AzureStorage;

namespace SingleNode
{
    internal class Program
    {
        private static IDistributedLock locker;

        private static void Main(string[] args)
        {
            var storageKey = ConfigurationManager.AppSettings["storageKey"];

            Task.Run(async () =>
            {
                locker = AzureStorageDistributedLock.Create(
                    "parallelwork1",
                    options =>
                    {
                        options.ConnectionString = storageKey;
                        options.Directory = "singlenode";
                    });

                var tasks = new List<Task>
                {
                    DoWork(1),
                    DoWork(2),
                    DoWork(3),
                    DoWork(4),
                    DoWork(5),
                    DoWork(6),
                    DoWork(7),
                    DoWork(8)
                };

                await Task.WhenAll(tasks);
            }).Wait();

            Console.ReadLine();
        }

        private static Task DoWork(int number)
        {
            Console.WriteLine($"Node 1 work n {number} launched");

            return locker.ExecuteAsync(async context =>
            {
                Console.WriteLine($"Node 1 work n {number} starting");
                await Task.Delay(2000);
                Console.WriteLine($"Node 1 work n {number} finished");
            });
        }
    }
}