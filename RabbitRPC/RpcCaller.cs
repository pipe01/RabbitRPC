﻿using ClassImpl;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitRPC.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitRPC
{
    public sealed class RpcCaller : IDisposable
    {
        private readonly IDictionary<string, TaskCompletionSource<JsonElement>> RunningCalls = new Dictionary<string, TaskCompletionSource<JsonElement>>();
        private readonly bool DisposeChannel;
        private readonly EventingBasicConsumer Consumer;

        private bool IsDisposed;

        public IModel Channel { get; }
        public string QueueName { get; }
        public string CallbackQueueName { get; }

        public RpcCaller(IModel channel, string queueName, bool disposeChannel = false)
        {
            this.Channel = channel;
            this.QueueName = queueName;
            this.DisposeChannel = disposeChannel;

            channel.QueueDeclare(queueName);
            CallbackQueueName = channel.QueueDeclare().QueueName;

            this.Consumer = new EventingBasicConsumer(channel);
            this.Consumer.Received += this.Consumer_Received;

            channel.BasicConsume(Consumer, CallbackQueueName, autoAck: false);
        }

        private void Consumer_Received(object sender, BasicDeliverEventArgs e)
        {
            if (RunningCalls.TryGetValue(e.BasicProperties.CorrelationId, out var tcs))
            {
                var jsonDoc = JsonDocument.Parse(e.Body);

                tcs.TrySetResult(jsonDoc.RootElement);

                RunningCalls.Remove(e.BasicProperties.CorrelationId);
                Channel.BasicAck(e.DeliveryTag, false);
            }
        }

        public Task<object> Call(string method, Type returnType, params object[] args)
            => Call(method, returnType, CancellationToken.None, args);

        public Task Call(string method, params object[] args)
            => Call<object>(method, CancellationToken.None, args);

        public Task Call(string method, CancellationToken cancellationToken, params object[] args)
            => Call<object>(method, cancellationToken, args);

        public async Task<object> Call(string method, Type returnType, CancellationToken cancellationToken, params object[] args)
        {
            CheckDisposed();

            var props = Channel.CreateBasicProperties();
            props.ReplyTo = CallbackQueueName;
            props.CorrelationId = Guid.NewGuid().ToString();

            var tcs = new TaskCompletionSource<JsonElement>();
            using var _ = RunningCalls.AddThenRemove(props.CorrelationId, tcs);

            var req = new RpcRequest
            {
                MethodName = method,
                Arguments = args
            };

            Channel.BasicPublish("", QueueName, props, JsonSerializer.SerializeToUtf8Bytes(req));

            JsonElement returnElement;

            using (cancellationToken.Register(() => tcs.SetCanceled()))
                returnElement = await tcs.Task;

            if (returnElement.ValueKind == JsonValueKind.Null)
                return null;

#if NETSTANDARD2_1 || NETCOREAPP3_0
            IBufferWriter<byte> bufferWriter = new ArrayBufferWriter<byte>();
#else
            using var bufferWriter = new ArrayBufferWriter();
#endif

            using (var writer = new Utf8JsonWriter(bufferWriter))
                returnElement.WriteTo(writer);

            return JsonSerializer.Deserialize(bufferWriter.GetSpan(), returnType);
        }

        public async Task<T> Call<T>(string method, params object[] args)
            => (T)await Call(method, typeof(T), CancellationToken.None, args);

        public async Task<T> Call<T>(string method, CancellationToken cancellationToken, params object[] args)
            => (T)await Call(method, typeof(T), cancellationToken, args);

        /// <summary>
        /// Creates a proxy type for this queue. If <typeparamref name="T"/> is an interface, every method
        /// will be overriden to call <see cref="Call(string, Type, object[])"/>. If it isn't an interface,
        /// all of its virtual or abstract methods will be overriden instead.
        /// </summary>
        /// <typeparam name="T">The type of the proxy to create</typeparam>
        public T CreateProxy<T>()
        {
            CheckDisposed();

            var impl = new Implementer<T>();

            foreach (var item in impl.Methods.Where(o => o.DeclaringType == typeof(T)))
            {
                impl.Member<object>(item).Callback(args => Call(item.Name, item.ReturnType, args.Values.ToArray()).Result);
            }

            return impl.Finish();
        }

        public void Dispose()
        {
            CheckDisposed();

            Channel.BasicCancel(Consumer.ConsumerTag);
            Consumer.Received -= Consumer_Received;

            Channel.QueueDelete(CallbackQueueName);

            if (DisposeChannel)
                Channel.Dispose();

            IsDisposed = true;
        }

        private void CheckDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(null);
        }
    }
}
