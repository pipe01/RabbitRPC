using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitRPC.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace RabbitRPC
{
    /// <summary>
    /// Represents an RPC service whose methods a remote <see cref="RpcCaller"/> can call.
    /// </summary>
    public abstract class RpcService
    {
        /// <summary>
        /// The method that this service has.
        /// </summary>
        protected internal IDictionary<string, MethodInfo> Methods { get; }

        /// <summary>
        /// The queue name that will be used by default. This is equal to the type's full name, replacing dots
        /// with underscores.
        /// </summary>
        public string DefaultQueueName { get; }

        /// <summary>
        /// Instantiates a new <see cref="RpcService"/> instance.
        /// </summary>
        protected RpcService()
        {
            DefaultQueueName = this.GetType().FullName.Replace('.', '_');
            Methods = this.GetType().GetMethods().ToDictionary(o => o.Name);
        }

        /// <summary>
        /// Binds this service to a <paramref name="channel"/>, using the default queue name.
        /// </summary>
        /// <param name="channel">The channel to bind this service to</param>
        public void BindTo(IModel channel) => BindTo(channel, DefaultQueueName);

        /// <summary>
        /// Binds this service to a <paramref name="channel"/>, using a <paramref name="queueName"/>.
        /// </summary>
        /// <param name="channel">The channel to bind this service to</param>
        /// <param name="queueName">The queue name to bind this service to</param>
        public void BindTo(IModel channel, string queueName)
        {
            channel.QueueDeclare(queueName);

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (sender, e) =>
            {
                var body = Newtonsoft.Json.JsonConvert.DeserializeObject<RpcRequest>(Encoding.UTF8.GetString(e.Body));

                if (Methods.TryGetValue(body.MethodName, out var method))
                {
                    var methodParams = method.GetParameters();
                    var args = body.Arguments.Select((o, i) => Convert.ChangeType(o, methodParams[i].ParameterType)).ToArray();

                    var returnVal = method.Invoke(this, args);

                    var props = channel.CreateBasicProperties();
                    props.CorrelationId = e.BasicProperties.CorrelationId;

                    channel.BasicPublish("", e.BasicProperties.ReplyTo, props, JsonSerializer.SerializeToUtf8Bytes(returnVal));
                }
            };

            channel.BasicConsume(queueName, true, consumer);
        }
    }
}
