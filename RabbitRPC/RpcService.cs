using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace RabbitRPC
{
    public abstract class RpcService
    {
        protected internal readonly IDictionary<string, MethodInfo> Methods;

        public string DefaultQueueName { get; }

        protected RpcService()
        {
            DefaultQueueName = this.GetType().FullName.Replace('.', '_');
            Methods = this.GetType().GetMethods().ToDictionary(o => o.Name);
        }

        public void BindTo(IModel channel) => BindTo(channel, DefaultQueueName);

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
