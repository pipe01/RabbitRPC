﻿namespace RabbitRPC.Internal
{
    internal class RpcRequest
    {
        public string MethodName { get; set; }
        public object[] Arguments { get; set; }
    }
}
