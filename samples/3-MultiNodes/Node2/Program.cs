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
            Console.ForegroundColor = ConsoleColor.Green;
            Task.Run(async () => { await ExecuteWork(); }).Wait();

            Console.ReadLine();
        }

        private static async Task ExecuteWork()
        {
            IDistributedLock locker;
            var storageKey = ConfigurationManager.AppSettings["storageKey"];

            locker = await AzureStorageDistributedLock.Create(
                "parallelwork1",
                options =>
                {
                    options.ConnectionString = storageKey;
                    options.Directory = "nodes";
                });

            var tasks = new List<Task>
            {
                DoWork(locker, 1),
                DoWork(locker, 2),
                DoWork(locker, 3),
                DoWork(locker, 4),
                DoWork(locker, 5),
                DoWork(locker, 6),
                DoWork(locker, 7),
                DoWork(locker, 8)
            };

            await Task.WhenAll(tasks);
        }

        private static Task DoWork(IDistributedLock locker, int number)
        {
            Console.WriteLine($"Process 1 work n {number} launched");

            return locker.Execute(async () =>
            {
                Console.WriteLine($"Process 1 work n {number} starting");
                await Task.Delay(2000);
                Console.WriteLine($"Process 1 work n {number} finished");
            });
        }
    }
}