using System;
using System.IO;

namespace Flashnote.Services.Sync
{
    /// <summary>
    /// ノート/フォルダのローカルパス解決を一箇所に集約する。
    /// iOS版 SyncPathResolver.swift に対応。
    /// </summary>
    public static class SyncPathResolver
    {
        /// <summary>
        /// `%USERPROFILE%\Documents\Flashnote` のルートパス。
        /// 必ず Environment.GetFolderPath を使うため、
        /// Environment.SpecialFolder.MyDocuments.ToString() を書いてしまうバグが構造的に発生しない。
        /// </summary>
        public static string GetLocalNoteRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
        }

        /// <summary>
        /// サブフォルダ（相対パス、null/空ならルート）のローカルフルパスを返す。
        /// </summary>
        public static string GetLocalFolderPath(string subFolder, bool createIfMissing = false)
        {
            var root = GetLocalNoteRoot();
            var path = string.IsNullOrEmpty(subFolder) ? root : Path.Combine(root, subFolder);
            if (createIfMissing && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        /// <summary>
        /// ノート本体(.ankpls)のローカルフルパスを返す。
        /// </summary>
        public static string GetLocalNotePath(string subFolder, string noteName, bool createDirectoryIfMissing = false)
        {
            var folderPath = GetLocalFolderPath(subFolder, createDirectoryIfMissing);
            return Path.Combine(folderPath, $"{noteName}.ankpls");
        }

        /// <summary>
        /// ローカルのフルパスから、Flashnoteルートを基準にしたサブフォルダ相対パスを求める。
        /// ルート直下の場合や、Flashnoteルート外のパスの場合は null を返す。
        /// </summary>
        public static string GetSubFolderFromLocalPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;

            var root = GetLocalNoteRoot();
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relativePath = Path.GetRelativePath(root, fullPath);
            if (relativePath == "." || relativePath.StartsWith("."))
            {
                return null;
            }

            return relativePath;
        }
    }
}
