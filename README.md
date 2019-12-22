# RabbitRPC
[![nuget](https://img.shields.io/nuget/v/RabbitRPC.svg)](https://www.nuget.org/packages/RabbitRPC)

An RPC library that leverages RabbitMQ

## Usage

### Classic style

"Publisher" service:
```cs
public class MyService : RpcService
{
    public virtual int Multiply(int n, int f)
    {
        return n * f;
    }
}

IModel channel = /* usual rabbit channel creation */

var svc = new MyService();
svc.BindTo(channel, "MyService");
//or
svc.BindTo(channel); //In this case the queue name will default to the class' full name
```

"Consumer" service:
```cs
using var caller = new RpcCaller(channel, "MyService");

int result = await caller.Call("Multiply", 2, 5);
Assert.Equal(result, 10);
```

### Proxy style
Same publisher as before, but the consumer is slightly different:

```cs
using var caller = new RpcCaller(channel, "MyService");
MyService svc = caller.CreateProxy<MyService>();

int result = svc.Multiply(2, 5);
Assert.Equal(result, 10);
```
