using ClassImpl;
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
    /// <summary>
    /// Represents an RPC caller with the ability to call methods on a remote <see cref="RpcService"/>.
    /// </summary>
    public sealed class RpcCaller : IDisposable
    {
        private readonly IDictionary<string, TaskCompletionSource<byte[]>> RunningCalls = new Dictionary<string, TaskCompletionSource<byte[]>>();
        private readonly bool DisposeChannel;
        private readonly EventingBasicConsumer Consumer;

        private bool IsDisposed;

        /// <summary>
        /// The channel this caller uses.
        /// </summary>
        public IModel Channel { get; }

        /// <summary>
        /// The outbound queue name this caller uses.
        /// </summary>
        public string QueueName { get; }

        /// <summary>
        /// The callback queue name this caller uses.
        /// </summary>
        public string CallbackQueueName { get; }

        /// <summary>
        /// Instantiates a new <see cref="RpcCaller"/> instance.
        /// </summary>
        /// <param name="channel">The channel to use</param>
        /// <param name="queueName">The outbound queue name to use</param>
        /// <param name="disposeChannel">If true, <paramref name="channel"/> will get disposed when this <see cref="RpcCaller"/> instance is</param>
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
                tcs.TrySetResult(e.Body);

                RunningCalls.Remove(e.BasicProperties.CorrelationId);
                Channel.BasicAck(e.DeliveryTag, false);
            }
        }

        /// <summary>
        /// Calls <paramref name="method"/> on a remote service with <paramref name="args"/> and no return value.
        /// </summary>
        /// <param name="method">The remote method's name</param>
        /// <param name="args">The method's arguments</param>
        public Task Call(string method, params object[] args)
            => Call<object>(method, CancellationToken.None, args);

        /// <summary>
        /// Calls <paramref name="method"/> on a remote service with <paramref name="args"/> and no return value.
        /// </summary>
        /// <param name="method">The remote method's name</param>
        /// <param name="cancellationToken">The cancellation token to use for cancelling the call</param>
        /// <param name="args">The method's arguments</param>
        public Task Call(string method, CancellationToken cancellationToken, params object[] args)
            => Call<object>(method, cancellationToken, args);

        /// <summary>
        /// Calls <paramref name="method"/> on a remote service with <paramref name="args"/> that returns a value
        /// of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The return value's type</typeparam>
        /// <param name="method">The remote method's name</param>
        /// <param name="args">The method's arguments</param>
        public async Task<T> Call<T>(string method, params object[] args)
            => (T)await Call(method, typeof(T), CancellationToken.None, args);

        /// <summary>
        /// Calls <paramref name="method"/> on a remote service with <paramref name="args"/> that returns a value
        /// of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The return value's type</typeparam>
        /// <param name="method">The remote method's name</param>
        /// <param name="cancellationToken">The cancellation token to use for cancelling the call</param>
        /// <param name="args">The method's arguments</param>
        public async Task<T> Call<T>(string method, CancellationToken cancellationToken, params object[] args)
            => (T)await Call(method, typeof(T), cancellationToken, args);

        /// <summary>
        /// Calls <paramref name="method"/> on a remote service with <paramref name="args"/> that returns a value
        /// of type <paramref name="returnType"/>.
        /// </summary>
        /// <param name="method">The remote method's name</param>
        /// <param name="returnType">The return value's type</param>
        /// <param name="args">The method's arguments</param>
        public Task<object> Call(string method, Type returnType, params object[] args)
            => Call(method, returnType, CancellationToken.None, args);

        /// <summary>
        /// Calls <paramref name="method"/> on a remote service with <paramref name="args"/> that returns a value
        /// of type <paramref name="returnType"/>.
        /// </summary>
        /// <param name="method">The remote method's name</param>
        /// <param name="returnType">The return value's type</param>
        /// <param name="cancellationToken">The cancellation token to use for cancelling the call</param>
        /// <param name="args">The method's arguments</param>
        public async Task<object> Call(string method, Type returnType, CancellationToken cancellationToken, params object[] args)
        {
            CheckDisposed();

            var props = Channel.CreateBasicProperties();
            props.ReplyTo = CallbackQueueName;
            props.CorrelationId = Guid.NewGuid().ToString();

            var tcs = new TaskCompletionSource<byte[]>();
            using var _ = RunningCalls.AddThenRemove(props.CorrelationId, tcs);

            var req = new RpcRequest
            {
                MethodName = method,
                Arguments = args
            };

            Channel.BasicPublish("", QueueName, props, JsonSerializer.SerializeToUtf8Bytes(req));

            byte[] returnData;

            using (cancellationToken.Register(() => tcs.SetCanceled()))
                returnData = await tcs.Task;

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                returnType = returnType.GenericTypeArguments[0];

            return JsonSerializer.Deserialize(returnData, returnType);
        }

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
                var returnType = item.ReturnType;

                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                    returnType = returnType.GenericTypeArguments[0];

                impl.Member(item).Callback(args =>
                {
                    if (typeof(Task).IsAssignableFrom(item.ReturnType))
                    {
                        var callMethod = this
                                    .GetType()
#if NETSTANDARD2_1 || NETCOREAPP3_0
                                    .GetMethod("Call", 1, new[] { typeof(string), typeof(object[]) })
#else
                                    .GetMethods() 
                                    .Single(o => o.ContainsGenericParameters && o
                                        .GetParameters()
                                        .Select(o => o.ParameterType)
                                        .SequenceEqual(new[] { typeof(string), typeof(object[]) }))
#endif
                                    .MakeGenericMethod(returnType);

                        return callMethod.Invoke(this, new object[] { item.Name, args.Values.ToArray() });
                    }
                    else
                    {
                        return Call(item.Name, returnType, args.Values.ToArray()).Result;
                    }
                });
            }

            return impl.Finish();
        }

        /// <inheritdoc/>
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
