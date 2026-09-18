using System.Runtime.InteropServices;
using Ado.Domain;

namespace Ado.Platform;

internal sealed class LinuxCredentialStore : INativeCredentialStore
{
    private const string Secret = "libsecret-1.so.0", Glib = "libglib-2.0.so.0", Gobject = "libgobject-2.0.so.0", Gio = "libgio-2.0.so.0";

    public Task<string> ReadAsync(string service, string account, bool nonInteractive, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        IntPtr library = NativeLibrary.Load(Glib);
        IntPtr attributes = IntPtr.Zero, items = IntPtr.Zero, error = IntPtr.Zero, cancellable = IntPtr.Zero;
        var allocated = new List<IntPtr>();
        CancellationTokenRegistration registration = default;
        try
        {
            attributes = g_hash_table_new(NativeLibrary.GetExport(library, "g_str_hash"), NativeLibrary.GetExport(library, "g_str_equal"));
            void Attribute(string key, string value)
            {
                var keyPointer = Marshal.StringToCoTaskMemUTF8(key);
                allocated.Add(keyPointer);
                var valuePointer = Marshal.StringToCoTaskMemUTF8(value);
                allocated.Add(valuePointer);
                g_hash_table_insert(attributes, keyPointer, valuePointer);
            }
            Attribute("service", service);
            Attribute("account", account);
            cancellable = g_cancellable_new();
            registration = cancellationToken.Register(() => g_cancellable_cancel(cancellable));
            // libsecret/secret-types.h: ALL=1<<1, UNLOCK=1<<2, LOAD_SECRETS=1<<3.
            items = secret_service_search_sync(IntPtr.Zero, IntPtr.Zero, attributes,
                SearchFlags(nonInteractive), cancellable, out error);
            cancellationToken.ThrowIfCancellationRequested();
            if (error != IntPtr.Zero)
                throw new AdoException("credential_provider_unavailable", "Secret Service could not be reached or read. Check the session bus and keyring; headless CI should use stdin/environment injection.", ExitCode.Authentication);
            if (items == IntPtr.Zero)
                throw new AdoException("credential_not_found", "No Secret Service item matches the configured service and account attributes.", ExitCode.Authentication);
            var first = Marshal.PtrToStructure<GList>(items);
            if (first.Next != IntPtr.Zero)
                throw new AdoException("credential_ambiguous", "Multiple keyring items match this reference. Use unique service/account attributes.", ExitCode.Authentication);
            IntPtr value = secret_item_get_secret(first.Data);
            if (value == IntPtr.Zero)
                throw new AdoException("interaction_required", "The keyring item is locked or inaccessible. Unlock it interactively before non-interactive use.", ExitCode.Authentication);
            try
            {
                IntPtr data = secret_value_get(value, out nuint length);
                if (length > 65536) throw new AdoException("invalid_credential", "Keyring credential exceeds the input limit.", ExitCode.Authentication);
                return Marshal.PtrToStringUTF8(data, checked((int)length));
            }
            finally { secret_value_unref(value); }
        }
        finally
        {
            registration.Dispose();
            if (cancellable != IntPtr.Zero) g_object_unref(cancellable);
            for (var current = items; current != IntPtr.Zero;)
            {
                var node = Marshal.PtrToStructure<GList>(current);
                g_object_unref(node.Data);
                current = node.Next;
            }
            if (items != IntPtr.Zero) g_list_free(items);
            if (error != IntPtr.Zero) g_error_free(error);
            if (attributes != IntPtr.Zero) g_hash_table_destroy(attributes);
            foreach (var pointer in allocated) Marshal.FreeCoTaskMem(pointer);
            NativeLibrary.Free(library);
        }
    }, cancellationToken);

    internal static int SearchFlags(bool nonInteractive) => (1 << 1) | (1 << 3) | (nonInteractive ? 0 : 1 << 2);

    [StructLayout(LayoutKind.Sequential)] private struct GList { public IntPtr Data, Next, Previous; }
    [DllImport(Glib)] private static extern IntPtr g_hash_table_new(IntPtr hash, IntPtr equal);
    [DllImport(Glib)] private static extern int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);
    [DllImport(Glib)] private static extern void g_hash_table_destroy(IntPtr table);
    [DllImport(Glib)] private static extern void g_list_free(IntPtr list);
    [DllImport(Glib)] private static extern void g_error_free(IntPtr error);
    [DllImport(Gobject)] private static extern void g_object_unref(IntPtr value);
    [DllImport(Gio)] private static extern IntPtr g_cancellable_new();
    [DllImport(Gio)] private static extern void g_cancellable_cancel(IntPtr cancellable);
    [DllImport(Secret)] private static extern IntPtr secret_service_search_sync(IntPtr service, IntPtr schema, IntPtr attributes, int flags, IntPtr cancellable, out IntPtr error);
    [DllImport(Secret)] private static extern IntPtr secret_item_get_secret(IntPtr item);
    [DllImport(Secret)] private static extern IntPtr secret_value_get(IntPtr value, out nuint length);
    [DllImport(Secret)] private static extern void secret_value_unref(IntPtr value);
}
