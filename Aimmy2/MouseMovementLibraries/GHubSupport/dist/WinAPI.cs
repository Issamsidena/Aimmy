using System.Runtime.InteropServices;

namespace Aimmy2.MouseMovementLibraries.GHubSupport.dist
{
    internal class WinAPI
    {
        // SourceString is passed as a raw pointer on purpose: RtlInitUnicodeString only stores the
        // pointer inside the UNICODE_STRING, so the buffer has to stay alive after the call returns.
        // Marshalling a string here would hand the kernel a pointer to a temporary buffer that the
        // interop marshaller frees the moment this call returns.
        [DllImport("ntdll.dll")]
        public static extern void RtlInitUnicodeString(nint DestinationString, nint SourceString);

        [DllImport("ntdll.dll", ExactSpelling = true, SetLastError = true)]
        public static extern int NtCreateFile(
            out nint handle,
            int access,
            ref Properties.OBJECT_ATTRIBUTES objectAttributes,
            ref Properties.IO_STATUS_BLOCK ioStatus,
            nint allocSize,
            uint fileAttributes,
            int share,
            uint createDisposition,
            uint createOptions,
            nint eaBuffer,
            uint eaLength);

        [DllImport("ntdll.dll", ExactSpelling = true, SetLastError = true)]
        public static extern int NtDeviceIoControlFile(
            nint fileHandle,
            nint eventHandle,
            nint apcRoutine,
            nint ApcContext,
            ref Properties.IO_STATUS_BLOCK ioStatus,
            uint controlCode,
            ref Struct.MOUSE_IO inputBuffer,
            int inputBufferLength,
            nint outputBuffer,
            int outputBufferLength
        );

        [DllImport("ntdll.dll")]
        public static extern int ZwClose(nint h);
    }
}