using System;
using System.Text;
using MasselGUARD.Cli;
using SharpFuzz;

namespace MasselGUARD.Fuzz.WireGuardConf;

internal static class Program
{
    public static void Main(string[] args)
    {
        Fuzzer.Run(stream =>
        {
            var len = (int)Math.Min(stream.Length, 256 * 1024);
            var buf = new byte[len];
            stream.ReadExactly(buf, 0, len);
            var text = Encoding.UTF8.GetString(buf);
            try
            {
                global::MasselGUARD.Cli.WireGuardConf.Parse(text);
            }
            catch
            {
                /* parse errors expected */
            }

            try
            {
                global::MasselGUARD.Cli.WireGuardConf.Validate(text);
            }
            catch
            {
                /* validation errors expected */
            }
        });
    }
}
