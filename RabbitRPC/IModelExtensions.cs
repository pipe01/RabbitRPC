using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Threading.Tasks;

namespace RabbitRPC
{
    public static class IModelExtensions
    {
        public static async Task<byte[]> WaitForResponse(this IModel channel, string queueName, string correlationId, bool autoAck = false)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (_, e) =>
            {
                if (e.BasicProperties.CorrelationId == correlationId)
                    tcs.SetResult(e.Body);
            };

            string cTag = channel.BasicConsume(queueName, autoAck, consumer);

            var result = await tcs.Task;

            channel.BasicCancel(cTag);

            return result;
        }
    }
}
