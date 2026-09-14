using System.Diagnostics;
using Augit.Core.Files;

namespace Augit.App;

internal sealed partial class MainWindow
{
    private int _treeInteractionRevision;

    internal Task RefreshWorkspaceForTestAsync() => RefreshWorkspaceAsync();
    internal int LoadedTreeNodeCountForTest => _treeNodes.Count;
    internal Action? TreeLoadYieldedForTest { get; set; }
    internal nint TreeItemForTest(string path) => _treeNodes.Values.FirstOrDefault(
        node => node.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase))?.ItemHandle ?? 0;

    private bool IsCurrentTreeLoad(TreeNodeState node, int generation, Func<bool>? canApply) =>
        !_disposed && generation == _treeGeneration && canApply?.Invoke() != false
        && _treeNodes.TryGetValue(node.Id, out TreeNodeState? current) && ReferenceEquals(current, node);

    private async Task<bool> ApplyTreeEntriesAsync(TreeNodeState node, TreeNodeState[] children,
        IReadOnlyList<WorkspaceEntry> entries, int generation, Func<bool>? canApply)
    {
        Dictionary<string, TreeNodeState> existing = children.ToDictionary(child => child.FullPath, StringComparer.Ordinal);
        Dictionary<string, WorkspaceEntry> incoming = entries.ToDictionary(entry => entry.FullPath, StringComparer.Ordinal);
        nint firstVisible = GetFirstVisibleTreeNode()?.ItemHandle ?? 0;
        int interactionRevision = _treeInteractionRevision;
        Stopwatch slice = Stopwatch.StartNew();
        int operations = 0;
        // 删除最后一个占位子项会让 Win32 自动收起父目录；保留它直到真实子项应用完毕。
        nint placeholder = children.Length == 0
            ? NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewChild, node.ItemHandle)
            : 0;
        if (placeholder == 0 && entries.Count > 0) placeholder = InsertPlaceholder(node.ItemHandle);
        try
        {
            foreach (TreeNodeState child in children)
            {
                if (incoming.TryGetValue(child.FullPath, out WorkspaceEntry? entry)
                    && child.Name == entry.Name && child.IsDirectory == entry.IsDirectory && child.CanExpand == entry.CanExpand)
                    continue;
                SelectTreeRemovalNeighbor(child, incoming);
                if (child.IsDirectory) RemoveTreeDescendants(child);
                _treeNodes.Remove(child.Id);
                existing.Remove(child.FullPath);
                _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewDeleteItem, 0, child.ItemHandle);
                if (!await YieldIfNeededAsync()) return false;
            }

            nint previous = NativeMethods.TreeViewInsertFirst;
            foreach (WorkspaceEntry entry in entries)
            {
                if (!existing.TryGetValue(entry.FullPath, out TreeNodeState? child))
                {
                    child = new(_nextTreeNodeId++, entry.Name, entry.FullPath, entry.IsDirectory, entry.CanExpand, node.Id);
                    InsertTreeNode(child, node.ItemHandle, previous);
                    if (child.CanExpand) InsertPlaceholder(child.ItemHandle);
                    if (!await YieldIfNeededAsync()) return false;
                }
                previous = child.ItemHandle;
            }

            if (interactionRevision == _treeInteractionRevision && firstVisible != 0
                && GetTreeNodeFromItem(firstVisible) is not null)
                _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewSelectItem, NativeMethods.TreeViewFirstVisible, firstVisible);
            return IsCurrentTreeLoad(node, generation, canApply);
        }
        finally
        {
            if (placeholder != 0 && IsCurrentTreeLoad(node, generation, null))
                _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewDeleteItem, 0, placeholder);
        }

        async Task<bool> YieldIfNeededAsync()
        {
            if (++operations % 32 == 0 && slice.ElapsedMilliseconds >= 4)
            {
                TreeLoadYieldedForTest?.Invoke();
                await Task.Delay(1);
                slice.Restart();
            }
            return IsCurrentTreeLoad(node, generation, canApply);
        }
    }

    private void SelectTreeRemovalNeighbor(TreeNodeState removed, Dictionary<string, WorkspaceEntry> incoming)
    {
        TreeNodeState? selected = GetSelectedTreeNode();
        while (selected is not null && selected.Id != removed.Id)
            selected = _treeNodes.GetValueOrDefault(selected.ParentId);
        if (selected is null) return;

        // 优先选择仍存在的后继，其次前驱；组已为空时返回父目录，不抢走正文焦点。
        foreach (nuint direction in new nuint[] { 1, 2 }) // TVGN_NEXT、TVGN_PREVIOUS。
        {
            nint candidate = removed.ItemHandle;
            while ((candidate = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewGetNextItem, direction, candidate)) != 0)
            {
                TreeNodeState? neighbor = GetTreeNodeFromItem(candidate);
                if (neighbor is not null && incoming.TryGetValue(neighbor.FullPath, out WorkspaceEntry? entry)
                    && neighbor.Name == entry.Name && neighbor.IsDirectory == entry.IsDirectory && neighbor.CanExpand == entry.CanExpand)
                {
                    _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewSelectItem, NativeMethods.TreeViewCaret, candidate);
                    return;
                }
            }
        }
        if (_treeNodes.TryGetValue(removed.ParentId, out TreeNodeState? parent))
            _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewSelectItem, NativeMethods.TreeViewCaret, parent.ItemHandle);
    }
}
