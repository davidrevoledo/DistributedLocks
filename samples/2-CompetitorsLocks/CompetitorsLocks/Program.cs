using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;
using DistributedLock;
using DistributedLock.AzureStorage;

namespace ConsoleApp1
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Task.Run(async () =>
            {
                var tasks = new List<Task>
                {
                    Competitor1("worker 1"),
                    Competitor1("worker 2")
                };

                await Task.WhenAll(tasks);
            }).Wait();

            Console.ReadLine();
        }

        private static async Task Competitor1(string key)
        {
            IDistributedLock locker;
            var storageKey = ConfigurationManager.AppSettings["storageKey"];

            locker = await AzureStorageDistributedLock.Create(
                "parallelwork1",
                options =>
                {
                    options.ConnectionString = storageKey;
                    options.Directory = "workers";
                });

            var tasks = new List<Task>
            {
                DoWork(locker, 1, key),
                DoWork(locker, 2, key),
                DoWork(locker, 3, key),
                DoWork(locker, 4, key),
                DoWork(locker, 5, key),
                DoWork(locker, 6, key),
                DoWork(locker, 7, key),
                DoWork(locker, 8, key)
            };

            await Task.WhenAll(tasks);
        }

        private static Task DoWork(IDistributedLock locker, int number, string key)
        {
            Console.WriteLine($"Worker {key} work n {number} launched");

            return locker.Execute(async () =>
            {
                Console.WriteLine($"Worker {key} work n {number} starting");
                await Task.Delay(2000);
                Console.WriteLine($"Worker {key} work n {number} finished");
            });
        }
    }
}