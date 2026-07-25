using System.Runtime.InteropServices;

namespace Aimmy2.MouseMovementLibraries.GHubSupport.dist
{
    internal class Properties
    {
        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        public struct OBJECT_ATTRIBUTES : IDisposable
        {
            public int Length;
            public nint RootDirectory;
            private nint objectName;
            public uint Attributes;
            public nint SecurityDescriptor;
            public nint SecurityQualityOfService;

            public OBJECT_ATTRIBUTES(string name, uint attributes)
            {
                Length = 0;
                RootDirectory = nint.Zero;
                objectName = nint.Zero;
                Attributes = attributes;
                SecurityDescriptor = nint.Zero;
                SecurityQualityOfService = nint.Zero;

                Length = Marshal.SizeOf(this);
                objectName = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UNICODE_STRING)));

                // Own the string buffer ourselves: RtlInitUnicodeString just points UNICODE_STRING.Buffer
                // at it, and that pointer is handed to NtCreateFile long after this constructor returns.
                // It is released in Dispose (read back out of the UNICODE_STRING, so that no extra field
                // is added to this struct -- its layout and Length are passed straight to the kernel).
                nint nameBuffer = Marshal.StringToHGlobalUni(name);
                WinAPI.RtlInitUnicodeString(objectName, nameBuffer);
            }

            public void Dispose()
            {
                if (objectName != nint.Zero)
                {
                    UNICODE_STRING unicodeName = Marshal.PtrToStructure<UNICODE_STRING>(objectName);
                    if (unicodeName.Buffer != nint.Zero)
                    {
                        Marshal.FreeHGlobal(unicodeName.Buffer);
                    }

                    Marshal.FreeHGlobal(objectName);
                    objectName = nint.Zero;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        public struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public nint Buffer;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        public struct IO_STATUS_BLOCK
        {
            public uint Status;
            public nint Information;
        }
    }
}