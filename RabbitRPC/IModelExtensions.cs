using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Text;
using System.Threading.Tasks;

namespace RabbitRPC
{
    /// <summary>
    /// Extensions for <see cref="IModel"/>.
    /// </summary>
    public static class IModelExtensions
    {
        /// <summary>
        /// Waits for a message to arrive on <paramref name="channel"/> with <paramref name="correlationId"/>, and
        /// optionally automatically auto-acks, then returns the message's body.
        /// </summary>
        /// <param name="channel">The channel the queue belongs to</param>
        /// <param name="queueName">The queue the message will arrive on</param>
        /// <param name="correlationId">The correlation ID the message must have</param>
        /// <param name="autoAck">Whether to auto-ack the message on arrival</param>
        /// <returns>The received message's body.</returns>
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

        /// <summary>
        /// Publishes a message to <paramref name="exchange"/> with <paramref name="body"/>, then waits for a response using
        /// <see cref="WaitForResponse(IModel, string, string, bool)"/> and returns the received message's body.
        /// </summary>
        /// <param name="channel">The channel the exchange belongs to</param>
        /// <param name="exchange">The exchange to publish the message to</param>
        /// <param name="body">The sent message's body</param>
        /// <param name="callbackQueue">The queue to wait a response on. If <c>null</c>, a temporary queue is generated and deleted when the message arrives</param>
        /// <returns>The received message's body.</returns>
        public static async Task<byte[]> PublishAndGetResponse(this IModel channel, string exchange, byte[] body, string callbackQueue = null)
        {
            string queue = callbackQueue ?? channel.QueueDeclare();

            try
            {
                var props = channel.CreateBasicProperties();
                props.ReplyTo = queue;
                props.CorrelationId = Guid.NewGuid().ToString();

                channel.BasicPublish(exchange, "", props, body);

                return await channel.WaitForResponse(queue, props.CorrelationId, true);
            }
            finally
            {
                if (callbackQueue == null)
                    channel.QueueDelete(queue);
            }
        }

        /// <summary>
        /// Publishes a message to <paramref name="exchange"/> with <paramref name="body"/>, then waits for a response using
        /// <see cref="WaitForResponse(IModel, string, string, bool)"/> and returns the received message's body as a string.
        /// </summary>
        /// <param name="channel">The channel the exchange belongs to</param>
        /// <param name="exchange">The exchange to publish the message to</param>
        /// <param name="body">The sent message's body</param>
        /// <param name="callbackQueue">The queue to wait a response on. If <c>null</c>, a temporary queue is generated and deleted when the message arrives</param>
        /// <param name="encoding">The encoding to use when encoding and decoding strings, by default <see cref="Encoding.UTF8"/></param>
        /// <returns>The received message's body as a string</returns>
        public static async Task<string> PublishAndGetString(this IModel channel, string exchange, string body, string callbackQueue = null, Encoding encoding = null)
        {
            encoding ??= Encoding.UTF8;

            var resp = await channel.PublishAndGetResponse(exchange, encoding.GetBytes(body), callbackQueue);

            return encoding.GetString(resp);
        }
    }
}
