using System.Runtime.CompilerServices;

// The msi.dll imports are all LibraryImport (source-generated). Disabling runtime marshalling lets the
// generator pin char[] and byte[] buffers directly, as msi.dll writes UTF-16 text and raw bytes into them.
[assembly: DisableRuntimeMarshalling]
