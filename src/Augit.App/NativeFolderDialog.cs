using System.Runtime.InteropServices;

namespace Augit.App;

internal static class NativeFolderDialog
{
    private const uint PickFolders = 0x00000020;
    private const uint ForceFileSystem = 0x00000040;
    private const uint PathMustExist = 0x00000800;
    private const int Cancelled = unchecked((int)0x800704C7);
    private static readonly Guid FileOpenDialogClass = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");

    internal static string? SelectFolder(nint owner)
    {
        Type? dialogType = Type.GetTypeFromCLSID(FileOpenDialogClass, throwOnError: false);
        if (dialogType is null || Activator.CreateInstance(dialogType) is not IFileOpenDialog dialog)
        {
            throw new InvalidOperationException(UiText.FolderDialogUnavailable);
        }

        try
        {
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | PickFolders | ForceFileSystem | PathMustExist);
            dialog.SetTitle(UiText.SelectWorkspace);
            int result = dialog.Show(owner);
            if (result == Cancelled)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(result);
            dialog.GetResult(out IShellItem item);
            try
            {
                item.GetDisplayName(ShellDisplayName.FileSystemPath, out nint pathPointer);
                try
                {
                    return Marshal.PtrToStringUni(pathPointer);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pathPointer);
                }
            }
            finally
            {
                _ = Marshal.FinalReleaseComObject(item);
            }
        }
        finally
        {
            _ = Marshal.FinalReleaseComObject(dialog);
        }
    }

    private enum ShellDisplayName : uint
    {
        FileSystemPath = 0x80058000,
    }

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(nint parent);

        void SetFileTypes(uint count, nint filterSpecifications);

        void SetFileTypeIndex(uint index);

        void GetFileTypeIndex(out uint index);

        void Advise(nint events, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void SetDefaultFolder(IShellItem shellItem);

        void SetFolder(IShellItem shellItem);

        void GetFolder(out IShellItem shellItem);

        void GetCurrentSelection(out IShellItem shellItem);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string fileName);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string fileName);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem shellItem);

        void AddPlace(IShellItem shellItem, uint placement);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

        void Close(int result);

        void SetClientGuid(in Guid guid);

        void ClearClientData();

        void SetFilter(nint filter);
    }

    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog : IFileDialog
    {
        [PreserveSig]
        new int Show(nint parent);

        new void SetFileTypes(uint count, nint filterSpecifications);

        new void SetFileTypeIndex(uint index);

        new void GetFileTypeIndex(out uint index);

        new void Advise(nint events, out uint cookie);

        new void Unadvise(uint cookie);

        new void SetOptions(uint options);

        new void GetOptions(out uint options);

        new void SetDefaultFolder(IShellItem shellItem);

        new void SetFolder(IShellItem shellItem);

        new void GetFolder(out IShellItem shellItem);

        new void GetCurrentSelection(out IShellItem shellItem);

        new void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string fileName);

        new void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string fileName);

        new void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        new void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        new void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        new void GetResult(out IShellItem shellItem);

        new void AddPlace(IShellItem shellItem, uint placement);

        new void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

        new void Close(int result);

        new void SetClientGuid(in Guid guid);

        new void ClearClientData();

        new void SetFilter(nint filter);

        void GetResults(out nint items);

        void GetSelectedItems(out nint items);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint bindContext, in Guid handler, in Guid interfaceIdentifier, out nint result);

        void GetParent(out IShellItem parent);

        void GetDisplayName(ShellDisplayName name, out nint displayName);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IShellItem shellItem, uint hint, out int order);
    }
}
