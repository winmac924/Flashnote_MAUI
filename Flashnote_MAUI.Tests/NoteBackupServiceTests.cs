using System;
using System.IO;
using System.Linq;
using System.Threading;
using Flashnote.Services.Sync;

namespace Flashnote_MAUI.Tests
{
    public class NoteBackupServiceTests
    {
        private static string BackupRootForKey(string key) =>
            Path.Combine(SyncPathResolver.GetLocalNoteRoot(), ".backups", key);

        [Fact]
        public void BackupBeforeOverwrite_NonExistentFile_DoesNothing()
        {
            var service = new NoteBackupService();
            var key = $"key-{Guid.NewGuid()}";
            var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid()}.ankpls");

            service.BackupBeforeOverwrite(missingPath, key);

            Assert.False(Directory.Exists(BackupRootForKey(key)));
        }

        [Fact]
        public void BackupBeforeOverwrite_ExistingFile_CreatesBackupCopy()
        {
            var service = new NoteBackupService();
            var key = $"key-{Guid.NewGuid()}";
            var sourcePath = Path.Combine(Path.GetTempPath(), $"note-{Guid.NewGuid()}.ankpls");
            File.WriteAllText(sourcePath, "original content");

            try
            {
                service.BackupBeforeOverwrite(sourcePath, key);

                var backupDir = BackupRootForKey(key);
                Assert.True(Directory.Exists(backupDir));
                var backups = Directory.GetFiles(backupDir, "*.bak");
                Assert.Single(backups);
                Assert.Equal("original content", File.ReadAllText(backups[0]));
            }
            finally
            {
                File.Delete(sourcePath);
                CleanupBackupDir(key);
            }
        }

        [Fact]
        public void BackupBeforeOverwrite_MoreThanMaxGenerations_PrunesOldest()
        {
            var service = new NoteBackupService();
            var key = $"key-{Guid.NewGuid()}";
            var sourcePath = Path.Combine(Path.GetTempPath(), $"note-{Guid.NewGuid()}.ankpls");

            try
            {
                for (int i = 0; i < 5; i++)
                {
                    File.WriteAllText(sourcePath, $"content-{i}");
                    service.BackupBeforeOverwrite(sourcePath, key);
                    Thread.Sleep(15); // LastWriteTimeの解像度差を確実にするため
                }

                var backupDir = BackupRootForKey(key);
                var backups = Directory.GetFiles(backupDir, "*.bak");

                Assert.Equal(3, backups.Length); // MaxGenerations = 3
            }
            finally
            {
                File.Delete(sourcePath);
                CleanupBackupDir(key);
            }
        }

        private static void CleanupBackupDir(string key)
        {
            try
            {
                var dir = BackupRootForKey(key);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch
            {
                // テストのクリーンアップ失敗は無視
            }
        }
    }
}
