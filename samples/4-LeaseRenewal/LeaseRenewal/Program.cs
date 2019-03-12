using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;
using DistributedLocks;
using DistributedLocks.AzureStorage;

namespace LeaseRenewal
{
    internal class Program
    {
        private static IDistributedLock locker;

        private static void Main(string[] args)
        {
            var storageKey = ConfigurationManager.AppSettings["storageKey"];

            Task.Run(async () =>
            {
                locker = await AzureStorageDistributedLock.CreateAsync(
                    "parallelwork1",
                    options =>
                    {
                        options.ConnectionString = storageKey;
                        options.Directory = "leaserenewal";
                        options.LeaseDuration = TimeSpan.FromSeconds(30);
                    });

                var tasks = new List<Task>
                {
                    DoHugeWork(),
                };

                await Task.Delay(200); // wait until start the small work to be started in the second place always

                tasks.Add(DoSmallWork());

                await Task.WhenAll(tasks);
            }).Wait();

            Console.ReadLine();
        }

        private static Task DoHugeWork()
        {
            Console.WriteLine("Huge work launched");

            return locker.ExecuteAsync(async context =>
            {
                Console.WriteLine("Huge work starting");

                // stage 1 - 15 seconds
                await Task.Delay(15000);
                await context.RenewLeaseAsync(TimeSpan.FromSeconds(20));
                Console.WriteLine("Getting more time to get the work done");

                // stage 2 - 15 seconds
                await Task.Delay(15000);
                await context.RenewLeaseAsync(TimeSpan.FromSeconds(20));
                Console.WriteLine("Getting more time to get the work done");

                // stage 3 - 15 seconds
                await Task.Delay(15000);
                await context.RenewLeaseAsync(TimeSpan.FromSeconds(20));
                Console.WriteLine("Getting more time to get the work done");

                // stage 4 - 15 seconds
                await Task.Delay(15000);

                Console.WriteLine("Huge work finished");
            });
        }

        private static Task DoSmallWork()
        {
            Console.WriteLine("Small work launched");

            return locker.ExecuteAsync(async context =>
            {
                Console.WriteLine("Small work starting");
                await Task.Delay(1000);
                Console.WriteLine("Small work finished");
            });
        }
    }
}