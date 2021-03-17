﻿using Microsoft.Extensions.FileProviders;
using Smidge.Hashing;
using Smidge.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Smidge.Cache
{
    

    public class PhysicalFileCacheFileSystem : ICacheFileSystem
    {
        private readonly IHasher _hasher;
        private readonly IFileProvider _fileProvider;

        public PhysicalFileCacheFileSystem(IFileProvider cacheFileProvider, IHasher hasher)
        {
            _fileProvider = cacheFileProvider;
            _hasher = hasher;
        }

        public IFileInfo GetRequiredFileInfo(string filePath)
        {
            var fileInfo = _fileProvider.GetFileInfo(filePath);

            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException($"No such file exists {fileInfo.PhysicalPath ?? fileInfo.Name} (mapped from {filePath})", fileInfo.PhysicalPath ?? fileInfo.Name);
            }

            return fileInfo;
        }

        private string GetCompositeFilePath(ICacheBuster cacheBuster, CompressionType type, string filesetKey)
            => $"{cacheBuster.GetValue()}/{type.ToString()}/{filesetKey + ".s"}";

        public Task ClearCachedCompositeFileAsync(ICacheBuster cacheBuster, CompressionType type, string filesetKey)
        {
            var path = GetCompositeFilePath(cacheBuster, type, filesetKey);
            var file = _fileProvider.GetFileInfo(path);

            if (file.PhysicalPath == null)
                throw new InvalidOperationException("The IFileInfo object supplied is not compatible with this provider.");

            if (file.IsDirectory)
                throw new InvalidOperationException("The IFileInfo object supplied is a directory, not a file.");

            if (file.Exists)
            {
                File.Delete(file.PhysicalPath);
            }
            
            return Task.CompletedTask;
        }

        public IFileInfo GetCachedCompositeFile(ICacheBuster cacheBuster, CompressionType type, string filesetKey, out string filePath)
        {
            filePath = GetCompositeFilePath(cacheBuster, type, filesetKey);
            return _fileProvider.GetFileInfo(filePath);
        }

        public Task WriteFileAsync(string filePath, string contents)
        {
            if (string.IsNullOrEmpty(Path.GetExtension(filePath)))
                throw new InvalidOperationException("The path supplied must contain a file extension.");

            filePath = filePath.Replace('/', Path.DirectorySeparatorChar);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            using (var writer = File.CreateText(filePath))
            {
                writer.Write(contents);
            }
            return Task.CompletedTask;
        }

        public async Task WriteFileAsync(string filePath, Stream contents)
        {
            if (string.IsNullOrEmpty(Path.GetExtension(filePath)))
                throw new InvalidOperationException("The path supplied must contain a file extension.");

            filePath = filePath.Replace('/', Path.DirectorySeparatorChar);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            using (var newFile = File.Create(filePath))
            {
                await contents.CopyToAsync(newFile);
            }
        }

        /// <summary>
        /// This will return the cache file path for a given IWebFile depending on if it's being watched
        /// </summary>
        /// <param name="file"></param>
        /// <param name="fileWatchEnabled"></param>
        /// <param name="extension"></param>
        /// <param name="cacheBuster"></param>
        /// <returns></returns>
        public IFileInfo GetCacheFile(IWebFile file, Func<IFileInfo> sourceFile, bool fileWatchEnabled, string extension, ICacheBuster cacheBuster, out string filePath)
        {
            IFileInfo cacheFile;
            if (fileWatchEnabled)
            {
                //When file watching, the file path will be different since we'll hash twice:
                // * Hash normally, since this will be a static hash of the file name
                // * Hash with timestamp since this will be how we change it
                // This allows us to lookup the file's folder to store it's timestamped processed files

                //get the file hash without the extension
                var fileHash = _hasher.GetFileHash(file, string.Empty);
                var timestampedHash = _hasher.GetFileHash(file, sourceFile(), extension);

                filePath = $"{cacheBuster.GetValue()}/{fileHash}/{timestampedHash}";
                cacheFile = _fileProvider.GetFileInfo(filePath);
            }
            else
            {
                var fileHash = _hasher.GetFileHash(file, extension);

                filePath = $"{cacheBuster.GetValue()}/{fileHash}";
                cacheFile = _fileProvider.GetFileInfo(filePath);
            }

            return cacheFile;
        }
    }
}