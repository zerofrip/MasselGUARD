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
                WireGuardConf.Parse(text);
            }
            catch
            {
                /* parse errors expected */
            }

            try
            {
                WireGuardConf.Validate(text);
            }
            catch
            {
                /* validation errors expected */
            }
        });
    }
}
