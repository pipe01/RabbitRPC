namespace RabbitRPC
{
    public class RpcRequest
    {
        public string MethodName { get; set; }
        public object[] Arguments { get; set; }
    }
}
