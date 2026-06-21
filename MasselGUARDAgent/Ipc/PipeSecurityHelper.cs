using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.IO.Pipes;

namespace MasselGUARD.Agent.Ipc
{
    /// <summary>Named pipe ACL helpers — SY/BA full, AU read/write (beta default).</summary>
    public static class PipeSecurityHelper
    {
        public static PipeSecurity CreateDefault()
        {
            var strict = string.Equals(
                Environment.GetEnvironmentVariable("MASSELGUARD_STRICT_PIPE"),
                "1",
                StringComparison.Ordinal);

            var ps = new PipeSecurity();
            ps.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
            ps.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            if (!strict)
            {
                ps.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                    PipeAccessRights.ReadWrite,
                    AccessControlType.Allow));
            }

            return ps;
        }

        public static NamedPipeServerStream CreateSecureServer(
            string pipeName,
            PipeDirection direction,
            int maxInstances,
            PipeTransmissionMode transmissionMode,
            PipeOptions options)
        {
            return NamedPipeServerStreamAcl.Create(
                pipeName,
                direction,
                maxInstances,
                transmissionMode,
                options,
                65536,
                65536,
                CreateDefault());
        }
    }
}
