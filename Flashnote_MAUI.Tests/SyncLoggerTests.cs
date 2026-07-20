using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Flashnote.Services.Sync;

namespace Flashnote_MAUI.Tests
{
    public class SyncLoggerTests
    {
        private static string LogFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Flashnote", "logs", $"sync-{DateTime.UtcNow:yyyyMMdd}.jsonl");

        [Fact]
        public void Info_WritesJsonLineToLogFile()
        {
            var marker = Guid.NewGuid().ToString();

            SyncLogger.Info("UnitTest", $"info message {marker}", noteId: "note-1");

            var lines = File.ReadAllLines(LogFilePath);
            var line = lines.LastOrDefault(l => l.Contains(marker));
            Assert.NotNull(line);

            using var doc = JsonDocument.Parse(line!);
            Assert.Equal("info", doc.RootElement.GetProperty("Level").GetString());
            Assert.Equal("UnitTest", doc.RootElement.GetProperty("Phase").GetString());
            Assert.Equal("note-1", doc.RootElement.GetProperty("NoteId").GetString());
        }

        [Fact]
        public void Error_WithException_WritesExceptionMessage()
        {
            var marker = Guid.NewGuid().ToString();
            var ex = new InvalidOperationException($"boom {marker}");

            SyncLogger.Error("UnitTest", ex);

            var lines = File.ReadAllLines(LogFilePath);
            var line = lines.LastOrDefault(l => l.Contains(marker));
            Assert.NotNull(line);

            using var doc = JsonDocument.Parse(line!);
            Assert.Equal("error", doc.RootElement.GetProperty("Level").GetString());
            Assert.Contains(marker, doc.RootElement.GetProperty("Message").GetString());
        }

        [Fact]
        public void Write_NeverThrows_EvenWithNullMessage()
        {
            var ex = Record.Exception(() => SyncLogger.Warn("UnitTest", null!));

            Assert.Null(ex);
        }
    }
}
