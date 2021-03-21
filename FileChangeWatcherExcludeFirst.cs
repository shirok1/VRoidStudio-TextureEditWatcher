using System;
using System.IO;

namespace Shiroki.VRoidStudioPlugin.TextureEditWatcher
{
    public class FileChangeWatcherExcludeFirst : IDisposable
    {
        public readonly string FileName;

        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private bool _ignoreFirst = true;

        public FileChangeWatcherExcludeFirst(string path)
        {
            var fileInfo = new FileInfo(path);
            FileName = fileInfo.Name;

            Watcher = new FileSystemWatcher(fileInfo.DirectoryName ?? throw new InvalidOperationException())
            {
                Filter = fileInfo.Name,
                NotifyFilter = NotifyFilters.LastWrite,
                IncludeSubdirectories = false
            };

            void OnModified(object sender, FileSystemEventArgs args)
            {
                if (_ignoreFirst)
                    _ignoreFirst = false;
                else
                    Modified?.Invoke(sender, args);
            }

            Watcher.Created += OnModified;
            Watcher.Changed += OnModified;
            Watcher.EnableRaisingEvents = true;
        }

        public FileSystemWatcher Watcher { get; }

        public void Dispose()
        {
            Watcher?.Dispose();
        }

        public event FileSystemEventHandler Modified;
    }
}