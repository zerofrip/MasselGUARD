using System;
using System.Text;
using System.Text.Json;
using MasselGUARD.Agent.Ipc;
using SharpFuzz;

namespace MasselGUARD.Fuzz.AgentIpc;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static void Main(string[] args)
    {
        Fuzzer.Run(stream =>
        {
            var len = (int)Math.Min(stream.Length, 64 * 1024);
            var buf = new byte[len];
            stream.ReadExactly(buf, 0, len);
            var line = Encoding.UTF8.GetString(buf);
            try
            {
                JsonSerializer.Deserialize<IpcRequest>(line, JsonOpts);
            }
            catch
            {
                /* invalid JSON-RPC expected */
            }
        });
    }
}
