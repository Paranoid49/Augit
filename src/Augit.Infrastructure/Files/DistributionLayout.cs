using System;
using System.Collections.Generic;
using System.IO;

namespace Augit.Infrastructure.Files;

/// <summary>
/// 定位随程序分发的资源目录。
/// </summary>
/// <remarks>
/// 程序有两种布局：开发布局下可执行文件位于仓库子目录中，资源目录在仓库根目录；
/// 发布布局下资源目录就在可执行文件旁边，且不存在仓库标记文件。
/// 因此必须先在起始目录直接查找，只有找不到时才向上回溯到仓库根目录。
/// 旧实现只做向上回溯，便携版没有仓库标记文件，启动时会直接失败。
/// </remarks>
public static class DistributionLayout
{
    /// <summary>
    /// 相对给定起始目录向上查找候选资源目录。
    /// </summary>
    /// <param name="startDirectory">起始目录，通常是可执行文件所在目录。</param>
    /// <param name="candidateRelativePaths">按优先级排列的候选相对路径。</param>
    /// <param name="repositoryMarkerFileName">仓库根目录的标记文件名，用于回溯时判断层级。</param>
    /// <returns>找到的绝对路径；全部候选都不存在时返回 <c>null</c>。</returns>
    public static string? FindAncestorDirectory(
        string startDirectory,
        IReadOnlyList<string> candidateRelativePaths,
        string repositoryMarkerFileName)
    {
        ArgumentNullException.ThrowIfNull(startDirectory);
        ArgumentNullException.ThrowIfNull(candidateRelativePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryMarkerFileName);
        if (candidateRelativePaths.Count == 0)
        {
            throw new ArgumentException("至少需要一个候选相对路径。", nameof(candidateRelativePaths));
        }

        DirectoryInfo? directory = new(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            // 发布布局：候选目录直接位于当前层级。
            foreach (string relative in candidateRelativePaths)
            {
                string candidate = Path.Combine(directory.FullName, relative);
                if (Directory.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            // 开发布局：只有仓库根目录才继续向上回溯，避免误命中无关的上层目录。
            if (File.Exists(Path.Combine(directory.FullName, repositoryMarkerFileName)))
            {
                foreach (string relative in candidateRelativePaths)
                {
                    string candidate = Path.Combine(directory.FullName, relative);
                    if (Directory.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }

                return null;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
