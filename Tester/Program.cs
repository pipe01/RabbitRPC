using RabbitMQ.Client;
using RabbitRPC;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tester
{
    public class MyService : RpcService
    {
        public virtual int Test(int a)
        {
            //Thread.Sleep(5000);
            return 0;
        }
    }

    public static class Program
    {
        static async Task Main(string[] args)
        {
            var factory = new ConnectionFactory
            {
                HostName = "localhost",
                UserName = "user",
                Password = "1234"
            };
            var conn = factory.CreateConnection();
            var channel = conn.CreateModel();

            var svc1 = new MyService();
            svc1.BindTo(channel);
            var svc2 = new MyService();
            svc2.BindTo(channel);

            var caller = new RpcCaller(channel, svc1.DefaultQueueName);
            await caller.Call("Test", 5);

            Console.WriteLine("Done");
            Thread.Sleep(Timeout.InfiniteTimeSpan);
        }
    }
}
