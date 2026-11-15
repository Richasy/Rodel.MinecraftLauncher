// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace Rodel.MinecraftLauncher.Core.Screenshots;

/// <summary>Uses the Windows shell's recycle operation and refuses a non-recycling delete at its actual effect boundary.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsRecycleBin
{
    internal static Task<string> RecycleAsync(string path, Action validate, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Recycle(path, validate, ct)); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { completion.SetCanceled(ct); }
            catch (Exception error) { completion.SetException(error); }
        })
        { IsBackground = true, Name = "Screenshot recycle" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static unsafe string Recycle(string path, Action validate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        PInvoke.CoInitializeEx(null, COINIT.COINIT_APARTMENTTHREADED).ThrowOnFailure();
        IFileOperation* operation = null;
        IShellItem* item = null;
        IFileOperationProgressSink* callback = null;
        IUnknown* unknown = null;
        var sink = new RecycleSink(validate, ct);
        try
        {
            FileOperation.CreateInstance(out operation).ThrowOnFailure();
            PInvoke.SHCreateItemFromParsingName(path, null, IShellItem.IID_Guid, out var rawItem).ThrowOnFailure();
            item = (IShellItem*)rawItem;
            unknown = (IUnknown*)SinkWrappers.Instance.GetOrCreateComInterfaceForObject(sink, CreateComInterfaceFlags.None);
            unknown->QueryInterface(out callback).ThrowOnFailure();
            operation->SetOperationFlags(FILEOPERATION_FLAGS.FOFX_RECYCLEONDELETE | FILEOPERATION_FLAGS.FOFX_EARLYFAILURE
                | FILEOPERATION_FLAGS.FOF_NO_UI | FILEOPERATION_FLAGS.FOF_NORECURSION | FILEOPERATION_FLAGS.FOF_NO_CONNECTED_ELEMENTS);
            operation->DeleteItem(item, callback);
            try { operation->PerformOperations(); }
            catch when (sink.Failure is not null) { ExceptionDispatchInfo.Capture(sink.Failure).Throw(); }

            if (sink.Failure is not null) ExceptionDispatchInfo.Capture(sink.Failure).Throw();
            if (sink.RecycledPath is { } recycled) return recycled;
            ct.ThrowIfCancellationRequested();
            throw new IOException("Windows did not report a completed recycle operation. Check the original file and Recycle Bin before retrying.");
        }
        finally
        {
            if (operation is not null) operation->Release();
            if (callback is not null) callback->Release();
            if (unknown is not null) unknown->Release();
            if (item is not null) item->Release();
            GC.KeepAlive(sink);
            PInvoke.CoUninitialize();
        }
    }

    internal static bool IsRecycling(uint flags) => (flags & (uint)_TRANSFER_SOURCE_FLAGS.TSF_DELETE_RECYCLE_IF_POSSIBLE) != 0;

    private sealed unsafe class RecycleSink(Action validate, CancellationToken ct) : IFileOperationProgressSink.Interface
    {
        internal Exception? Failure { get; private set; }
        internal string? RecycledPath { get; private set; }

        /// <inheritdoc/>
        public HRESULT PreDeleteItem(uint dwFlags, IShellItem* psiItem)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!IsRecycling(dwFlags))
                    throw new LauncherOperationException("SCREENSHOT_RECYCLE_UNAVAILABLE", "Windows cannot recycle this screenshot. The file was not deleted.");
                validate();
                return HRESULT.S_OK;
            }
            catch (Exception error)
            {
                Failure = error;
                return HRESULT.E_ABORT;
            }
        }

        /// <inheritdoc/>
        public HRESULT PostDeleteItem(uint dwFlags, IShellItem* psiItem, HRESULT hrDelete, IShellItem* psiNewlyCreated)
        {
            try
            {
                hrDelete.ThrowOnFailure();
                if (!IsRecycling(dwFlags) || psiNewlyCreated is null)
                    throw new IOException("Windows did not provide a recycle receipt. Check the original file and Recycle Bin before retrying.");
                psiNewlyCreated->GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var name);
                try { RecycledPath = name.ToString(); }
                finally { Marshal.FreeCoTaskMem((nint)name.Value); }

                return HRESULT.S_OK;
            }
            catch (Exception error)
            {
                Failure ??= error;
                return HRESULT.E_ABORT;
            }
        }

        /// <inheritdoc/>
        public HRESULT UpdateProgress(uint iWorkTotal, uint iWorkSoFar) => ct.IsCancellationRequested ? HRESULT.E_ABORT : HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT StartOperations() => HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT FinishOperations(HRESULT hrResult) => HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT PreRenameItem(uint flags, IShellItem* item, PCWSTR name) => HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT PostRenameItem(uint flags, IShellItem* item, PCWSTR name, HRESULT result, IShellItem* created) => HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT PreMoveItem(uint flags, IShellItem* item, IShellItem* destination, PCWSTR name) => HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT PostMoveItem(uint flags, IShellItem* item, IShellItem* destination, PCWSTR name, HRESULT result, IShellItem* created) => HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT PreCopyItem(uint flags, IShellItem* item, IShellItem* destination, PCWSTR name) => HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT PostCopyItem(uint flags, IShellItem* item, IShellItem* destination, PCWSTR name, HRESULT result, IShellItem* created) => HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT PreNewItem(uint flags, IShellItem* destination, PCWSTR name) => HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT PostNewItem(uint flags, IShellItem* destination, PCWSTR name, PCWSTR template, uint attributes, HRESULT result, IShellItem* created) => HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT ResetTimer() => HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT PauseTimer() => HRESULT.S_OK;
        /// <inheritdoc/>
        public HRESULT ResumeTimer() => HRESULT.S_OK;
    }

    private sealed unsafe class SinkWrappers : ComWrappers
    {
        internal static SinkWrappers Instance { get; } = new();
        private static readonly ComInterfaceEntry* Entry = CreateEntry();

        private static ComInterfaceEntry* CreateEntry()
        {
            var table = (IFileOperationProgressSink.Vtbl*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(SinkWrappers), sizeof(IFileOperationProgressSink.Vtbl));
            IFileOperationProgressSink.PopulateVTable(table);
            GetIUnknownImpl(out var query, out var add, out var release);
            table->QueryInterface_1 = (delegate* unmanaged[Stdcall]<IFileOperationProgressSink*, Guid*, void**, HRESULT>)query;
            table->AddRef_2 = (delegate* unmanaged[Stdcall]<IFileOperationProgressSink*, uint>)add;
            table->Release_3 = (delegate* unmanaged[Stdcall]<IFileOperationProgressSink*, uint>)release;
            var entry = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(SinkWrappers), sizeof(ComInterfaceEntry));
            entry->IID = IFileOperationProgressSink.IID_Guid;
            entry->Vtable = (nint)table;
            return entry;
        }

        /// <inheritdoc/>
        protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
        {
            if (obj is not RecycleSink) throw new NotSupportedException();
            count = 1;
            return Entry;
        }

        /// <inheritdoc/>
        protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) => throw new NotSupportedException();
        /// <inheritdoc/>
        protected override void ReleaseObjects(IEnumerable objects) => throw new NotSupportedException();
    }
}