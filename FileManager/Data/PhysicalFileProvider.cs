﻿using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using SfFileService.FileManager.Base;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace SfFileService.FileManager.PhysicalFileProvider
{
    public class PhysicalFileProvider : PhysicalFileProviderBase
    {
        protected string contentRootPath = string.Empty;
        protected string[] allowedExtension = new string[] { "*" };
        AccessDetails AccessDetails = new AccessDetails();
        private string rootName = string.Empty;
        protected string hostPath = string.Empty;
        protected string hostName = string.Empty;
        private string accessMessage = string.Empty;

        public PhysicalFileProvider()
        {
        }

        public void RootFolder(string name)
        {
            this.contentRootPath = name;
            this.hostName = new Uri(contentRootPath).Host;
            if (!string.IsNullOrEmpty(this.hostName))
            {
                this.hostPath = Path.DirectorySeparatorChar + this.hostName + Path.DirectorySeparatorChar + contentRootPath.Substring((contentRootPath.ToLower().IndexOf(this.hostName) + this.hostName.Length + 1));
            }
        }

        public void SetRules(AccessDetails details)
        {
            this.AccessDetails = details;
            DirectoryInfo root = new DirectoryInfo(this.contentRootPath);
            this.rootName = root.Name;
        }

        public virtual FileManagerResponse GetFiles(string path, bool showHiddenItems, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse readResponse = new FileManagerResponse();
            try
            {
                if (path == null)
                {
                    path = string.Empty;
                }
                String fullPath = (contentRootPath + path);
                DirectoryInfo directory = new DirectoryInfo(fullPath);
                string[] extensions = this.allowedExtension;
                FileManagerDirectoryContent cwd = new FileManagerDirectoryContent();
                string rootPath = string.IsNullOrEmpty(this.hostPath) ? this.contentRootPath : new DirectoryInfo(this.hostPath).FullName;
                string directName = directory.Parent != null ? directory.Parent.FullName : string.Empty;
                string parentPath = string.IsNullOrEmpty(this.hostPath) ? directName : new DirectoryInfo(this.hostPath + (path != "/" ? path : "")).Parent!.FullName;
                cwd.Name = string.IsNullOrEmpty(this.hostPath) ? directory.Name : new DirectoryInfo(this.hostPath + path).Name;
                cwd.Size = 0;
                cwd.IsFile = false;
                cwd.DateModified = directory.LastWriteTime;
                cwd.DateCreated = directory.CreationTime;
                cwd.HasChild = CheckChild(directory.FullName);
                cwd.Type = directory.Extension;
                cwd.FilterPath = GetRelativePath(rootPath, parentPath + Path.DirectorySeparatorChar);
                cwd.Permission = GetPathPermission(path);
                readResponse.CWD = cwd;
                if (!hasAccess(directory.FullName) || (cwd.Permission != null && !cwd.Permission.Read))
                {
                    readResponse.Files = null;
                    accessMessage = cwd.Permission != null ? cwd.Permission.Message : string.Empty;
                    throw new UnauthorizedAccessException("'" + cwd.Name + "' is not accessible. You need permission to perform the read action.");
                }
                readResponse.Files = ReadDirectories(directory, extensions, showHiddenItems, data);
                readResponse.Files = readResponse.Files.Concat(ReadFiles(directory, extensions, showHiddenItems, data));
                return readResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("is not accessible. You need permission") ? "401" : "417";
                if ((er.Code == "401") && !string.IsNullOrEmpty(accessMessage)) { er.Message = accessMessage; }
                readResponse.Error = er;
                return readResponse;
            }
        }

        protected virtual IEnumerable<FileManagerDirectoryContent> ReadFiles(DirectoryInfo directory, string[] extensions, bool showHiddenItems, params FileManagerDirectoryContent[] data)
        {
            try
            {
                FileManagerResponse readFiles = new FileManagerResponse();
                if (!showHiddenItems)
                {
                    IEnumerable<FileManagerDirectoryContent> files = extensions.SelectMany(directory.GetFiles).Where(f => (f.Attributes & FileAttributes.Hidden) == 0)
                            .Select(file => new FileManagerDirectoryContent
                            {
                                Name = file.Name,
                                IsFile = true,
                                Size = file.Length,
                                DateModified = file.LastWriteTime,
                                DateCreated = file.CreationTime,
                                HasChild = false,
                                Type = file.Extension,
                                FilterPath = GetRelativePath(this.contentRootPath, directory.FullName),
                                Permission = GetPermission(directory.FullName, file.Name, true)
                            });
                    readFiles.Files = files;
                }
                else
                {
                    IEnumerable<FileManagerDirectoryContent> files = extensions.SelectMany(directory.GetFiles)
                            .Select(file => new FileManagerDirectoryContent
                            {
                                Name = file.Name,
                                IsFile = true,
                                Size = file.Length,
                                DateModified = file.LastWriteTime,
                                DateCreated = file.CreationTime,
                                HasChild = false,
                                Type = file.Extension,
                                FilterPath = GetRelativePath(this.contentRootPath, directory.FullName),
                                Permission = GetPermission(directory.FullName, file.Name, true)
                            });
                    readFiles.Files = (IEnumerable<FileManagerDirectoryContent>)files;
                }
                return readFiles.Files;
            }
            catch (Exception)
            {
                throw;
            }
        }

        protected string GetRelativePath(string rootPath, string fullPath)
        {
            if (!String.IsNullOrEmpty(rootPath) && !String.IsNullOrEmpty(fullPath))
            {
                DirectoryInfo rootDirectory;
                if (!string.IsNullOrEmpty(this.hostName))
                {
                    if (rootPath.Contains(this.hostName) || rootPath.ToLower().Contains(this.hostName) || rootPath.ToUpper().Contains(this.hostName))
                    {
                        rootPath = rootPath.Substring(rootPath.IndexOf(this.hostName, StringComparison.CurrentCultureIgnoreCase) + this.hostName.Length);
                    }
                    if (fullPath.Contains(this.hostName) || fullPath.ToLower().Contains(this.hostName) || fullPath.ToUpper().Contains(this.hostName))
                    {
                        fullPath = fullPath.Substring(fullPath.IndexOf(this.hostName, StringComparison.CurrentCultureIgnoreCase) + this.hostName.Length);
                    }
                    rootDirectory = new DirectoryInfo(rootPath);
                    fullPath = new DirectoryInfo(fullPath).FullName;
                    rootPath = new DirectoryInfo(rootPath).FullName;
                }
                else
                {
                    rootDirectory = new DirectoryInfo(rootPath);
                }
                if (rootDirectory.FullName.Substring(rootDirectory.FullName.Length - 1) == Path.DirectorySeparatorChar.ToString())
                {
                    if (fullPath.Contains(rootDirectory.FullName))
                    {
                        return fullPath.Substring(rootPath.Length - 1);
                    }
                }
                else if (fullPath.Contains(rootDirectory.FullName + Path.DirectorySeparatorChar))
                {
                    return Path.DirectorySeparatorChar + fullPath.Substring(rootPath.Length + 1);
                }
            }
            return String.Empty;
        }


        protected virtual IEnumerable<FileManagerDirectoryContent> ReadDirectories(DirectoryInfo directory, string[] extensions, bool showHiddenItems, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse readDirectory = new FileManagerResponse();
            try
            {
                if (!showHiddenItems)
                {
                    IEnumerable<FileManagerDirectoryContent> directories = directory.GetDirectories().Where(f => (f.Attributes & FileAttributes.Hidden) == 0)
                            .Select(subDirectory => new FileManagerDirectoryContent
                            {
                                Name = subDirectory.Name,
                                Size = 0,
                                IsFile = false,
                                DateModified = subDirectory.LastWriteTime,
                                DateCreated = subDirectory.CreationTime,
                                HasChild = CheckChild(subDirectory.FullName),
                                Type = subDirectory.Extension,
                                FilterPath = GetRelativePath(this.contentRootPath, directory.FullName),
                                Permission = GetPermission(directory.FullName, subDirectory.Name, false)
                            });
                    readDirectory.Files = directories;
                }
                else
                {
                    IEnumerable<FileManagerDirectoryContent> directories = directory.GetDirectories().Select(subDirectory => new FileManagerDirectoryContent
                    {
                        Name = subDirectory.Name,
                        Size = 0,
                        IsFile = false,
                        DateModified = subDirectory.LastWriteTime,
                        DateCreated = subDirectory.CreationTime,
                        HasChild = CheckChild(subDirectory.FullName),
                        Type = subDirectory.Extension,
                        FilterPath = GetRelativePath(this.contentRootPath, directory.FullName),
                        Permission = GetPermission(directory.FullName, subDirectory.Name, false)
                    });
                    readDirectory.Files = directories;
                }
                return readDirectory.Files;
            }
            catch (Exception )
            {
                throw;
            }
        }
        public virtual FileManagerResponse Create(string path, string name, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse createResponse = new FileManagerResponse();
            try
            {

                AccessPermission PathPermission = GetPathPermission(path);

                if (PathPermission != null && (!PathPermission.Read || !PathPermission.WriteContents))
                {
                    accessMessage = PathPermission.Message;
                    throw new UnauthorizedAccessException("'" + this.getFileNameFromPath(this.rootName + path) + "' is not accessible. You need permission to perform the writeContents action.");
                }

                string newDirectoryPath = Path.Combine(contentRootPath + path, name);

                bool directoryExist = Directory.Exists(newDirectoryPath);

                if (directoryExist)
                {
                    DirectoryInfo exist = new DirectoryInfo(newDirectoryPath);
                    ErrorDetails er = new ErrorDetails();
                    er.Code = "400";
                    er.Message = "A file or folder with the name " + exist.Name.ToString() + " already exists.";
                    createResponse.Error = er;

                    return createResponse;
                }

                string physicalPath = GetPath(path);
                Directory.CreateDirectory(newDirectoryPath);
                DirectoryInfo directory = new DirectoryInfo(newDirectoryPath);
                FileManagerDirectoryContent CreateData = new FileManagerDirectoryContent();
                CreateData.Name = directory.Name;
                CreateData.IsFile = false;
                CreateData.Size = 0;
                CreateData.DateModified = directory.LastWriteTime;
                CreateData.DateCreated = directory.CreationTime;
                CreateData.HasChild = CheckChild(directory.FullName);
                CreateData.Type = directory.Extension;
                CreateData.Permission = GetPermission(physicalPath, directory.Name, false);
                FileManagerDirectoryContent[] newData = new FileManagerDirectoryContent[] { CreateData };
                createResponse.Files = newData;
                return createResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("is not accessible. You need permission") ? "401" : "417";
                if ((er.Code == "401") && !string.IsNullOrEmpty(accessMessage)) { er.Message = accessMessage; }
                createResponse.Error = er;
                return createResponse;
            }
        }

        public virtual FileManagerResponse Details(string path, string[] names, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse getDetailResponse = new FileManagerResponse();
            FileDetails detailFiles = new FileDetails();
            int namesLength = 0;
            try
            {
                namesLength = names != null ? names.Length : namesLength;
                var namesVal = names != null ? names[0] : string.Empty;
                if (namesLength == 0 || namesLength == 1)
                {
                    if (path == null) { path = string.Empty; };
                    string fullPath = "";
                    if (namesLength == 0)
                    {
                        fullPath = (contentRootPath + path.Substring(0, path.Length - 1));
                    }
                    else if (string.IsNullOrEmpty(namesVal))
                    {
                        fullPath = (contentRootPath + path);
                    }
                    else
                    {
                        fullPath = Path.Combine(contentRootPath + path, namesVal);
                    }
                    string physicalPath = GetPath(path);
                    DirectoryInfo directory = new DirectoryInfo(fullPath);
                    FileInfo info = new FileInfo(fullPath);
                    FileDetails fileDetails = new FileDetails();
                    DirectoryInfo baseDirectory = new DirectoryInfo(string.IsNullOrEmpty(this.hostPath) ? this.contentRootPath : this.hostPath);
                    fileDetails.Name = info.Name == "" ? directory.Name : info.Name;
                    fileDetails.IsFile = (File.GetAttributes(fullPath) & FileAttributes.Directory) != FileAttributes.Directory;
                    fileDetails.Size = (File.GetAttributes(fullPath) & FileAttributes.Directory) != FileAttributes.Directory ? byteConversion(info.Length).ToString() : byteConversion(GetDirectorySize(new DirectoryInfo(fullPath), 0)).ToString();
                    fileDetails.Created = info.CreationTime;
                    string parentDirectory = baseDirectory.Parent != null ? baseDirectory.Parent.FullName : string.Empty;
                    fileDetails.Location = GetRelativePath(string.IsNullOrEmpty(this.hostName) ? parentDirectory : parentDirectory + Path.DirectorySeparatorChar, info.FullName).Substring(1);
                    fileDetails.Modified = info.LastWriteTime;
                    fileDetails.Permission = GetPermission(physicalPath, fileDetails.Name, fileDetails.IsFile);
                    detailFiles = fileDetails;
                }
                else
                {
                    bool isVariousFolders = false;
                    string relativePath = "";
                    string previousPath = "";
                    string previousName = "";
                    FileDetails fileDetails = new FileDetails();
                    fileDetails.Size = "0";
                    DirectoryInfo baseDirectory = new DirectoryInfo(string.IsNullOrEmpty(this.hostPath) ? this.contentRootPath : this.hostPath);
                    string parentPath = baseDirectory.Parent != null? baseDirectory.Parent.FullName : string.Empty;
                    string baseDirectoryParentPath = string.IsNullOrEmpty(this.hostName) ? parentPath : parentPath + Path.DirectorySeparatorChar;
                    for (int i = 0; i < namesLength; i++)
                    {
                        string fullPath = "";
                        string nameValue = names != null ? names[i] : string.Empty;
                        if (nameValue == null)
                        {
                            fullPath = (contentRootPath + path);
                        }
                        else
                        {
                            fullPath = Path.Combine(contentRootPath + path, nameValue);
                        }
                        FileInfo info = new FileInfo(fullPath);
                        string dataName = data != null ? data[i].Name : string.Empty;
                        fileDetails.Name = previousName == "" ? previousName = dataName : previousName = previousName + ", " + dataName;
                        fileDetails.Size = (long.Parse(fileDetails.Size) + (((File.GetAttributes(fullPath) & FileAttributes.Directory) != FileAttributes.Directory) ? info.Length : GetDirectorySize(new DirectoryInfo(fullPath), 0))).ToString();
                        string directInfo = info.Directory != null ? info.Directory.FullName : string.Empty;
                        relativePath = GetRelativePath(baseDirectoryParentPath, directInfo);
                        previousPath = previousPath == "" ? relativePath : previousPath;
                        if (previousPath == relativePath && !isVariousFolders)
                        {
                            previousPath = relativePath;
                        }
                        else
                        {
                            isVariousFolders = true;
                        }
                    }
                    fileDetails.Size = byteConversion(long.Parse(fileDetails.Size)).ToString();
                    fileDetails.MultipleFiles = true;
                    detailFiles = fileDetails;
                }
                getDetailResponse.Details = detailFiles;
                return getDetailResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("is not accessible. You need permission") ? "401" : "417";
                getDetailResponse.Error = er;
                return getDetailResponse;
            }
        }

        public virtual FileManagerResponse Delete(string path, string[] names, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse DeleteResponse = new FileManagerResponse();
            List<FileManagerDirectoryContent> removedFiles = new List<FileManagerDirectoryContent>();
            try
            {
                string physicalPath = GetPath(path);
                string result = String.Empty;
                int namesLength = 0;
                namesLength = names != null ? names.Length : namesLength;
                for (int i = 0; i < namesLength; i++)
                {
                    var namesVal = names != null ? names[i] : string.Empty;
                    bool IsFile = !IsDirectory(physicalPath, namesVal);

                    AccessPermission permission = GetPermission(physicalPath, namesVal, IsFile);

                    if (permission != null && (!permission.Read || !permission.Write))
                    {
                        accessMessage = permission.Message;
                        throw new UnauthorizedAccessException("'" + this.getFileNameFromPath(this.rootName + path + namesVal) + "' is not accessible.  you need permission to perform the write action.");
                    }
                }
                FileManagerDirectoryContent removingFile;
                for (int i = 0; i < namesLength; i++)
                {
                    var namesVal = names != null ? names[i] : string.Empty;
                    string fullPath = Path.Combine((contentRootPath + path), namesVal);
                    DirectoryInfo directory = new DirectoryInfo(fullPath);
                    if (!string.IsNullOrEmpty(namesVal))
                    {
                        FileAttributes attr = File.GetAttributes(fullPath);
                        removingFile = GetFileDetails(fullPath);
                        //detect whether its a directory or file
                        if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            result = DeleteDirectory(fullPath);
                        }
                        else
                        {
                            try
                            {
                                File.Delete(fullPath);
                            }
                            catch (Exception e)
                            {
                                if (e.GetType().Name == "UnauthorizedAccessException")
                                {
                                    result = fullPath;
                                }
                                else
                                {
                                    throw ;
                                }
                            }
                        }
                        if (result != string.Empty)
                        {
                            break;

                        }
                        removedFiles.Add(removingFile);
                    }
                    else
                    {
                        throw new ArgumentNullException("name should not be null");
                    }
                }
                DeleteResponse.Files = removedFiles;
                if (result != String.Empty)
                {
                    string deniedPath = result.Substring(this.contentRootPath.Length);
                    ErrorDetails er = new ErrorDetails();
                    er.Message = "'" + this.getFileNameFromPath(deniedPath) + "' is not accessible.  you need permission to perform the write action.";
                    er.Code = "401";
                    if ((er.Code == "401") && !string.IsNullOrEmpty(accessMessage)) { er.Message = accessMessage; }
                    DeleteResponse.Error = er;
                    return DeleteResponse;
                }
                else
                {
                    return DeleteResponse;
                }
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("is not accessible. You need permission") ? "401" : "417";
                if ((er.Code == "401") && !string.IsNullOrEmpty(accessMessage)) { er.Message = accessMessage; }
                DeleteResponse.Error = er;
                return DeleteResponse;
            }
        }

        public virtual FileManagerResponse Rename(string path, string name, string newName, bool replace = false, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse renameResponse = new FileManagerResponse();
            try
            {
                string physicalPath = GetPath(path);
                bool IsFile = !IsDirectory(physicalPath, name);

                AccessPermission permission = GetPermission(physicalPath, name, IsFile);
                if (permission != null && (!permission.Read || !permission.Write))
                {
                    accessMessage = permission.Message;
                    throw new UnauthorizedAccessException();
                }

                string tempPath = (contentRootPath + path);
                string oldPath = Path.Combine(tempPath, name);
                string newPath = Path.Combine(tempPath, newName);
                FileAttributes attr = File.GetAttributes(oldPath);

                FileInfo info = new FileInfo(oldPath);
                bool isFile = (File.GetAttributes(oldPath) & FileAttributes.Directory) != FileAttributes.Directory;
                if (isFile)
                {
                    if (File.Exists(newPath) && !oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        FileInfo exist = new FileInfo(newPath);
                        ErrorDetails er = new ErrorDetails();
                        er.Code = "400";
                        er.Message = "Cannot rename " + exist.Name.ToString() + " to " + newName + ": destination already exists.";
                        renameResponse.Error = er;
                        return renameResponse;
                    }
                    info.MoveTo(newPath);
                }
                else
                {
                    bool directoryExist = Directory.Exists(newPath);
                    if (directoryExist && !oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        DirectoryInfo exist = new DirectoryInfo(newPath);
                        ErrorDetails er = new ErrorDetails();
                        er.Code = "400";
                        er.Message = "Cannot rename " + exist.Name.ToString() + " to " + newName + ": destination already exists.";
                        renameResponse.Error = er;

                        return renameResponse;
                    }
                    else if (oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        tempPath = Path.Combine(tempPath + "Syncfusion_TempFolder");
                        Directory.Move(oldPath, tempPath);
                        Directory.Move(tempPath, newPath);
                    }
                    else
                    {
                        Directory.Move(oldPath, newPath);
                    }
                }
                FileManagerDirectoryContent[] addedData = new[]{
                        GetFileDetails(newPath)
                    };
                renameResponse.Files = addedData;
                return renameResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = (e.GetType().Name == "UnauthorizedAccessException") ? "'" + this.getFileNameFromPath(this.rootName + path + name) + "' is not accessible. You need permission to perform the write action." : e.Message.ToString();
                er.Code = er.Message.Contains("is not accessible. You need permission") ? "401" : "417";
                if ((er.Code == "401") && !string.IsNullOrEmpty(accessMessage)) { er.Message = accessMessage; }
                renameResponse.Error = er;
                return renameResponse;
            }
        }

        public virtual FileManagerResponse Copy(string path, string targetPath, string[] names, string[] renameFiles, FileManagerDirectoryContent targetData, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse copyResponse = new FileManagerResponse();
            try
            {
                string result = String.Empty;
                int namesLength = names != null ? names.Length : 0;
                if (renameFiles == null)
                {
                    renameFiles = new string[0];
                }
                string physicalPath = GetPath(path);
                for (int i = 0; i < namesLength; i++)
                {
                    string nameValue = names != null ? names[i] : string.Empty;
                    bool IsFile = !IsDirectory(physicalPath, nameValue);

                    AccessPermission permission = GetPermission(physicalPath, nameValue, IsFile);

                    if (permission != null && (!permission.Read || !permission.Copy))
                    {
                        accessMessage = permission.Message;
                        throw new UnauthorizedAccessException("'" + this.getFileNameFromPath(this.rootName + path + nameValue) + "' is not accessible. You need permission to perform the copy action.");
                    }
                }

                AccessPermission PathPermission = GetPathPermission(targetPath);

                if (PathPermission != null && (!PathPermission.Read || !PathPermission.WriteContents))
                {
                    accessMessage = PathPermission.Message;
                    throw new UnauthorizedAccessException("'" + this.getFileNameFromPath(this.rootName + targetPath) + "' is not accessible. You need permission to perform the writeContents action.");
                }


                List<string> existFiles = new List<string>();
                List<string> missingFiles = new List<string>();
                List<FileManagerDirectoryContent> copiedFiles = new List<FileManagerDirectoryContent>();
                string tempPath = path;
                for (int i = 0; i < namesLength; i++)
                {
                    string fullname = names != null ? names[i] : string.Empty;
                    string nameValue = names != null ? names[i] : string.Empty;
                    int name = nameValue.LastIndexOf("/");
                    if (name >= 0)
                    {
                        path = tempPath + nameValue.Substring(0, name + 1);
                        nameValue = nameValue.Substring(name + 1);
                    }
                    else
                    {
                        path = tempPath;
                    }
                    string itemPath = Path.Combine(contentRootPath + path, nameValue);
                    if (Directory.Exists(itemPath) || File.Exists(itemPath))
                    {
                        if ((File.GetAttributes(itemPath) & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            string directoryName = nameValue;
                            string oldPath = Path.Combine(contentRootPath + path, directoryName);
                            string newPath = Path.Combine(contentRootPath + targetPath, directoryName);
                            bool exist = Directory.Exists(newPath);
                            if (exist)
                            {
                                int index = -1;
                                if (renameFiles.Length > 0)
                                {
                                    index = Array.FindIndex(renameFiles, row => row.Contains(directoryName));
                                }
                                if ((newPath == oldPath) || (index != -1))
                                {
                                    newPath = DirectoryRename(newPath);
                                    result = DirectoryCopy(oldPath, newPath);
                                    if (result != String.Empty) { break; }
                                    FileManagerDirectoryContent detail = GetFileDetails(newPath);
                                    detail.PreviousName = nameValue;
                                    copiedFiles.Add(detail);
                                }
                                else
                                {
                                    existFiles.Add(fullname);
                                }
                            }
                            else
                            {
                                result = DirectoryCopy(oldPath, newPath);
                                if (result != String.Empty) { break; }
                                FileManagerDirectoryContent detail = GetFileDetails(newPath);
                                detail.PreviousName = nameValue;
                                copiedFiles.Add(detail);
                            }
                        }
                        else
                        {
                            string fileName = nameValue;
                            string oldPath = Path.Combine(contentRootPath + path, fileName);
                            string newPath = Path.Combine(contentRootPath + targetPath, fileName);
                            bool fileExist = System.IO.File.Exists(newPath);
                            try
                            {

                                if (fileExist)
                                {
                                    int index = -1;
                                    if (renameFiles.Length > 0)
                                    {
                                        index = Array.FindIndex(renameFiles, row => row.Contains(fileName));
                                    }
                                    if ((newPath == oldPath) || (index != -1))
                                    {
                                        newPath = FileRename(newPath, fileName);
                                        File.Copy(oldPath, newPath);
                                        FileManagerDirectoryContent detail = GetFileDetails(newPath);
                                        detail.PreviousName = nameValue;
                                        copiedFiles.Add(detail);
                                    }
                                    else
                                    {
                                        existFiles.Add(fullname);
                                    }
                                }
                                else
                                {
                                    if (renameFiles.Length > 0)
                                    {
                                        File.Delete(newPath);
                                    }
                                    File.Copy(oldPath, newPath);
                                    FileManagerDirectoryContent detail = GetFileDetails(newPath);
                                    detail.PreviousName = nameValue;
                                    copiedFiles.Add(detail);
                                }
                            }
                            catch (Exception e)
                            {
                                if (e.GetType().Name == "UnauthorizedAccessException")
                                {
                                    result = newPath;
                                    break;
                                }
                                else
                                {
                                    throw ;
                                }
                            }
                        }
                    }
                    else
                    {
                        missingFiles.Add(nameValue);
                    }
                }
                copyResponse.Files = copiedFiles;
                if (result != String.Empty)
                {
                    string deniedPath = result.Substring(this.contentRootPath.Length);
                    ErrorDetails er = new ErrorDetails();
                    er.Message = "'" + this.getFileNameFromPath(deniedPath) + "' is not accessible. You need permission to perform the copy action.";
                    er.Code = "401";
                    copyResponse.Error = er;
                    return copyResponse;
                }

                if (existFiles.Count > 0)
                {
                    ErrorDetails er = new ErrorDetails();
                    er.FileExists = existFiles;
                    er.Code = "400";
                    er.Message = "File Already Exists";
                    copyResponse.Error = er;
                }
                if (missingFiles.Count == 0)
                {
                    return copyResponse;
                }
                else
                {
                    string namelist = missingFiles[0];
                    for (int k = 1; k < missingFiles.Count; k++)
                    {
                        namelist = namelist + ", " + missingFiles[k];
                    }
                    throw new FileNotFoundException(namelist + " not found in given location.");
                }
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("is not accessible. You need permission") ? "401" : "417";
                if ((er.Code == "401") && !string.IsNullOrEmpty(accessMessage)) { er.Message = accessMessage; }
                er.FileExists = copyResponse.Error?.FileExists;
                copyResponse.Error = er;
                return copyResponse;
            }
        }

        public virtual FileManagerResponse Move(string path, string targetPath, string[] names, string[] renameFiles, FileManagerDirectoryContent targetData, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse moveResponse = new FileManagerResponse();
            try
            {
                string result = String.Empty;
                int namesLength = 0;
                namesLength = names != null ? names.Length : namesLength;
                if (renameFiles == null)
                {
                    renameFiles = new string[0];
                }
                string physicalPath = GetPath(path);
                for (int i = 0; i < namesLength; i++)
                {
                    string namesValue = names != null ? names[i] : result;
                    bool IsFile = !IsDirectory(physicalPath, namesValue);

                    AccessPermission permission = GetPermission(physicalPath, namesValue, IsFile);

                    if (permission != null && (!permission.Read || !permission.Write))
                    {
                        accessMessage = permission.Message;
                        throw new UnauthorizedAccessException("'" + this.getFileNameFromPath(this.rootName + path + namesValue) + "' is not accessible. You need permission to perform the write action.");
                    }
                }

                AccessPermission PathPermission = GetPathPermission(targetPath);

                if (PathPermission != null && (!PathPermission.Read || !PathPermission.WriteContents))
                {
                    accessMessage = PathPermission.Message;
                    throw new UnauthorizedAccessException("'" + this.getFileNameFromPath(this.rootName + targetPath) + "' is not accessible. You need permission to perform the writeContents action.");
                }

                List<string> existFiles = new List<string>();
                List<string> missingFiles = new List<string>();
                List<FileManagerDirectoryContent> movedFiles = new List<FileManagerDirectoryContent>();
                string tempPath = path;
                for (int i = 0; i < namesLength; i++)
                {
                    string fullName = names != null ? names[i] : result;
                    int name = fullName.LastIndexOf("/");
                    if (name >= 0)
                    {
                        path = tempPath + fullName.Substring(0, name + 1);
                        fullName = fullName.Substring(name + 1);
                    }
                    else
                    {
                        path = tempPath;
                    }
                    string itemPath = Path.Combine(contentRootPath + path, fullName);
                    if (Directory.Exists(itemPath) || File.Exists(itemPath))
                    {
                        if ((File.GetAttributes(itemPath) & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            string directoryName = fullName;
                            string oldPath = Path.Combine(contentRootPath + path, directoryName);
                            string newPath = Path.Combine(contentRootPath + targetPath, directoryName);
                            bool exist = Directory.Exists(newPath);
                            if (exist)
                            {
                                int index = -1;
                                if (renameFiles.Length > 0)
                                {
                                    index = Array.FindIndex(renameFiles, row => row.Contains(directoryName));
                                }
                                if ((newPath == oldPath) || (index != -1))
                                {
                                    newPath = DirectoryRename(newPath);
                                    result = DirectoryCopy(oldPath, newPath);
                                    if (result != String.Empty) { break; }
                                    bool isExist = Directory.Exists(oldPath);
                                    if (isExist)
                                    {
                                        result = DeleteDirectory(oldPath);
                                        if (result != String.Empty) { break; }
                                    }
                                    FileManagerDirectoryContent detail = GetFileDetails(newPath);
                                    detail.PreviousName = fullName;
                                    movedFiles.Add(detail);
                                }
                                else
                                {
                                    existFiles.Add(fullName);
                                }
                            }
                            else
                            {
                                result = DirectoryCopy(oldPath, newPath);
                                if (result != String.Empty) { break; }
                                bool isExist = Directory.Exists(oldPath);
                                if (isExist)
                                {
                                    result = DeleteDirectory(oldPath);
                                    if (result != String.Empty) { break; }
                                }
                                FileManagerDirectoryContent detail = GetFileDetails(newPath);
                                detail.PreviousName = fullName;
                                movedFiles.Add(detail);
                            }
                        }
                        else
                        {
                            string fileName = fullName;
                            string oldPath = Path.Combine(contentRootPath + path, fileName);
                            string newPath = Path.Combine(contentRootPath + targetPath, fileName);
                            bool fileExist = File.Exists(newPath);
                            try
                            {

                                if (fileExist)
                                {
                                    int index = -1;
                                    if (renameFiles.Length > 0)
                                    {
                                        index = Array.FindIndex(renameFiles, row => row.Contains(fileName));
                                    }
                                    if ((newPath == oldPath) || (index != -1))
                                    {
                                        newPath = FileRename(newPath, fileName);
                                        File.Copy(oldPath, newPath);
                                        bool isExist = File.Exists(oldPath);
                                        if (isExist)
                                        {
                                            File.Delete(oldPath);
                                        }
                                        FileManagerDirectoryContent detail = GetFileDetails(newPath);
                                        detail.PreviousName = fullName;
                                        movedFiles.Add(detail);
                                    }
                                    else
                                    {
                                        existFiles.Add(fullName);
                                    }
                                }
                                else
                                {
                                    File.Copy(oldPath, newPath);
                                    bool isExist = File.Exists(oldPath);
                                    if (isExist)
                                    {
                                        File.Delete(oldPath);
                                    }
                                    FileManagerDirectoryContent detail = GetFileDetails(newPath);
                                    detail.PreviousName = fullName;
                                    movedFiles.Add(detail);
                                }

                            }
                            catch (Exception e)
                            {
                                if (e.GetType().Name == "UnauthorizedAccessException")
                                {
                                    result = newPath;
                                    break;
                                }
                                else
                                {
                                    throw;
                                }
                            }
                        }
                    }
                    else
                    {
                        missingFiles.Add(fullName);
                    }
                }
                moveResponse.Files = movedFiles;
                if (result != String.Empty)
                {
                    string deniedPath = result.Substring(this.contentRootPath.Length);
                    ErrorDetails er = new ErrorDetails();
                    er.Message = "'" + this.getFileNameFromPath(deniedPath) + "' is not accessible. You need permission to perform this action.";
                    er.Code = "401";
                    moveResponse.Error = er;
                    return moveResponse;
                }
                if (existFiles.Count > 0)
                {
                    ErrorDetails er = new ErrorDetails();
                    er.FileExists = existFiles;
                    er.Code = "400";
                    er.Message = "File Already Exists";
                    moveResponse.Error = er;
                }
                if (missingFiles.Count == 0)
                {
                    return moveResponse;
                }
                else
                {
                    string namelist = missingFiles[0];
                    for (int k = 1; k < missingFiles.Count; k++)
                    {
                        namelist = namelist + ", " + missingFiles[k];
                    }
                    throw new FileNotFoundException(namelist + " not found in given location.");
                }
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails
                {
                    Message = e.Message.ToString(),
                    Code = e.Message.ToString().Contains("is not accessible. You need permission") ? "401" : "417",
                    FileExists = moveResponse.Error?.FileExists
                };
                if ((er.Code == "401") && !string.IsNullOrEmpty(accessMessage)) { er.Message = accessMessage; }
                moveResponse.Error = er;
                return moveResponse;
            }
        }

        public virtual FileManagerResponse Search(string path, string searchString, bool showHiddenItems = false, bool caseSensitive = false, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse searchResponse = new FileManagerResponse();
            try
            {
                if (path == null) { path = string.Empty; };
                string searchWord = searchString;
                string searchPath = (this.contentRootPath + path);
                DirectoryInfo directory = new DirectoryInfo(this.contentRootPath + path);
                FileManagerDirectoryContent cwd = new FileManagerDirectoryContent();
                cwd.Name = directory.Name;
                cwd.Size = 0;
                cwd.IsFile = false;
                cwd.DateModified = directory.LastWriteTime;
                cwd.DateCreated = directory.CreationTime;
                string rootPath = string.IsNullOrEmpty(this.hostPath) ? this.contentRootPath : new DirectoryInfo(this.hostPath).FullName;
                string directoryName = directory.Parent != null ? directory.Parent.FullName : string.Empty;
                string parentPath = string.IsNullOrEmpty(this.hostPath) ? directoryName : new DirectoryInfo(this.hostPath + (path != "/" ? path : "")).Parent!.FullName;
                cwd.HasChild = CheckChild(directory.FullName);
                cwd.Type = directory.Extension;
                cwd.FilterPath = GetRelativePath(rootPath, parentPath + Path.DirectorySeparatorChar);
                cwd.Permission = GetPathPermission(path);
                if (cwd.Permission != null && !cwd.Permission.Read)
                {
                    accessMessage = cwd.Permission.Message;
                    throw new UnauthorizedAccessException("'" + this.getFileNameFromPath(this.rootName + path) + "' is not accessible. You need permission to perform the read action.");
                }
                searchResponse.CWD = cwd;

                List<FileManagerDirectoryContent> foundedFiles = new List<FileManagerDirectoryContent>();
                string[] extensions = this.allowedExtension;
                DirectoryInfo searchDirectory = new DirectoryInfo(searchPath);
                List<FileInfo> files = new List<FileInfo>();
                List<DirectoryInfo> directories = new List<DirectoryInfo>();
                if (showHiddenItems)
                {
                    IEnumerable<FileInfo> filteredFileList = GetDirectoryFiles(searchDirectory, files).
                        Where(item => new Regex(WildcardToRegex(searchString), (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)).IsMatch(item.Name));
                    IEnumerable<DirectoryInfo> filteredDirectoryList = GetDirectoryFolders(searchDirectory, directories).
                        Where(item => new Regex(WildcardToRegex(searchString), (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)).IsMatch(item.Name));
                    foreach (FileInfo file in filteredFileList)
                    {
                        var directName = String.IsNullOrEmpty(file.DirectoryName) ? string.Empty : file.DirectoryName;
                        FileManagerDirectoryContent fileDetails = GetFileDetails(Path.Combine(this.contentRootPath, directName, file.Name));
                        bool hasPermission = parentsHavePermission(fileDetails);
                        if (hasPermission)
                        {
                            foundedFiles.Add(fileDetails);
                        }
                    }
                    foreach (DirectoryInfo dir in filteredDirectoryList)
                    {
                        FileManagerDirectoryContent dirDetails = GetFileDetails(Path.Combine(this.contentRootPath, dir.FullName));
                        bool hasPermission = parentsHavePermission(dirDetails);
                        if (hasPermission)
                        {
                            foundedFiles.Add(dirDetails);
                        }
                    }
                }
                else
                {
                    IEnumerable<FileInfo> filteredFileList = GetDirectoryFiles(searchDirectory, files).
                       Where(item => new Regex(WildcardToRegex(searchString), (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)).IsMatch(item.Name) && (item.Attributes & FileAttributes.Hidden) == 0);
                    IEnumerable<DirectoryInfo> filteredDirectoryList = GetDirectoryFolders(searchDirectory, directories).
                        Where(item => new Regex(WildcardToRegex(searchString), (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)).IsMatch(item.Name) && (item.Attributes & FileAttributes.Hidden) == 0);
                    foreach (FileInfo file in filteredFileList)
                    {
                        var directName = string.IsNullOrEmpty(file.DirectoryName) ? string.Empty : file.DirectoryName;
                        FileManagerDirectoryContent fileDetails = GetFileDetails(Path.Combine(this.contentRootPath, directName, file.Name));
                        bool hasPermission = parentsHavePermission(fileDetails);
                        if (hasPermission)
                        {
                            foundedFiles.Add(fileDetails);
                        }
                    }
                    foreach (DirectoryInfo dir in filteredDirectoryList)
                    {
                        FileManagerDirectoryContent dirDetails = GetFileDetails(Path.Combine(this.contentRootPath, dir.FullName));
                        bool hasPermission = parentsHavePermission(dirDetails);
                        if (hasPermission)
                        {
                            foundedFiles.Add(dirDetails);
                        }
                    }
                }
                searchResponse.Files = (IEnumerable<FileManagerDirectoryContent>)foundedFiles;
                return searchResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("is not accessible. You need permission") ? "401" : "417";
                if ((er.Code == "401") && !string.IsNullOrEmpty(accessMessage)) { er.Message = accessMessage; }
                searchResponse.Error = er;
                return searchResponse;
            }
        }

        protected String byteConversion(long fileSize)
        {
            try
            {
                string[] index = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
                if (fileSize == 0)
                {
                    return "0 " + index[0];
                }

                long bytes = Math.Abs(fileSize);
                int loc = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
                double num = Math.Round(bytes / Math.Pow(1024, loc), 1);
                return (Math.Sign(fileSize) * num).ToString() + " " + index[loc];
            }
            catch (Exception )
            {
                throw ;
            }
        }
        protected virtual string WildcardToRegex(string pattern)
        {
            return "^" + Regex.Escape(pattern)
                              .Replace(@"\*", ".*")
                              .Replace(@"\?", ".")
                       + "$";
        }

        public virtual FileStreamResult GetImage(string path, string id, bool allowCompress, ImageSize size, params FileManagerDirectoryContent[] data)
        {
            try
            {

                AccessPermission PathPermission = GetFilePermission(path);
                if (PathPermission != null && !PathPermission.Read)
                    return null!;
                String fullPath = (contentRootPath + path);
                FileStream fileStreamInput = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                FileStreamResult fileStreamResult = new FileStreamResult(fileStreamInput, "APPLICATION/octet-stream");
                return fileStreamResult;
            }
            catch (Exception)
            {
                return null!;
            }
        }


        public virtual FileManagerResponse Upload(string path, IList<IFormFile> uploadFiles, string action, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse uploadResponse = new FileManagerResponse();
            try
            {

                AccessPermission PathPermission = GetPathPermission(path);

                if (PathPermission != null && (!PathPermission.Read || !PathPermission.Upload))
                {
                    accessMessage = PathPermission.Message;
                    throw new UnauthorizedAccessException("'" + this.getFileNameFromPath(this.rootName + path) + "' is not accessible. You need permission to perform the upload action.");
                }

                List<string> existFiles = new List<string>();
                foreach (IFormFile file in uploadFiles)
                {
                    if (uploadFiles != null)
                    {
                        var name = ContentDispositionHeaderValue.Parse(file.ContentDisposition).FileName.Trim().ToString();
                        var fullName = Path.Combine((this.contentRootPath + path), name);
                        if (action == "save")
                        {
                            if (!System.IO.File.Exists(fullName))
                            {
                                using (FileStream fs = System.IO.File.Create(fullName))
                                {
                                    file.CopyTo(fs);
                                    fs.Flush();
                                }
                            }
                            else
                            {
                                existFiles.Add(fullName);
                            }
                        }
                        else if (action == "remove")
                        {
                            if (System.IO.File.Exists(fullName))
                            {
                                System.IO.File.Delete(fullName);
                            }
                            else
                            {
                                ErrorDetails er = new ErrorDetails();
                                er.Code = "404";
                                er.Message = "File not found.";
                                uploadResponse.Error = er;
                            }
                        }
                        else if (action == "replace")
                        {
                            if (System.IO.File.Exists(fullName))
                            {
                                System.IO.File.Delete(fullName);
                            }
                            using (FileStream fs = System.IO.File.Create(fullName))
                            {
                                file.CopyTo(fs);
                                fs.Flush();
                            }
                        }
                        else if (action == "keepboth")
                        {
                            string newName = fullName;
                            int index = newName.LastIndexOf(".");
                            if (index >= 0)
                                newName = newName.Substring(0, index);
                            int fileCount = 0;
                            while (System.IO.File.Exists(newName + (fileCount > 0 ? "(" + fileCount.ToString() + ")" + Path.GetExtension(name) : Path.GetExtension(name))))
                            {
                                fileCount++;
                            }
                            newName = newName + (fileCount > 0 ? "(" + fileCount.ToString() + ")" : "") + Path.GetExtension(name);
                            using (FileStream fs = System.IO.File.Create(newName))
                            {
                                file.CopyTo(fs);
                                fs.Flush();
                            }
                        }
                    }
                }
                if (existFiles.Count != 0)
                {
                    ErrorDetails er = new ErrorDetails();
                    er.Code = "400";
                    er.Message = "File already exists.";
                    er.FileExists = existFiles;
                    uploadResponse.Error = er;
                }
                return uploadResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();

                er.Message = (e.GetType().Name == "UnauthorizedAccessException") ? "'" + this.getFileNameFromPath(path) + "' is not accessible. You need permission to perform the upload action." : e.Message.ToString();
                er.Code = er.Message.Contains("is not accessible. You need permission") ? "401" : "417";
                if ((er.Code == "401") && !string.IsNullOrEmpty(accessMessage)) { er.Message = accessMessage; }
                uploadResponse.Error = er;
                return uploadResponse;
            }
        }

        public virtual FileStreamResult Download(string path, string[] names, params FileManagerDirectoryContent[] data)
        {
            try
            {
                string physicalPath = GetPath(path);
                String fullPath;
                int count = 0;
                int nameLength = 0;
                nameLength = names != null ? names.Length : nameLength;
                for (int i = 0; i < nameLength; i++)
                {
                    var namesValue = names != null ? names[i] : string.Empty;
                    bool IsFile = !IsDirectory(physicalPath, namesValue);

                    AccessPermission FilePermission = GetPermission(physicalPath, namesValue, IsFile);

                    if (FilePermission != null && (!FilePermission.Read || !FilePermission.Download))
                        throw new UnauthorizedAccessException("'" + this.rootName + path + namesValue + "' is not accessible. Access is denied.");

                    fullPath = Path.Combine(contentRootPath + path, namesValue);
                    if ((File.GetAttributes(fullPath) & FileAttributes.Directory) != FileAttributes.Directory)
                    {
                        count++;
                    }
                }
                if (count == nameLength)
                {
                    return DownloadFile(path, names);
                }
                else
                {
                    return DownloadFolder(path, names!, count);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private FileStreamResult fileStreamResult;
        protected virtual FileStreamResult DownloadFile(string path, string[] names = null)
        {
            try
            {
                path = path != null ? Path.GetDirectoryName(path) : path;
                string tempPath = Path.Combine(Path.GetTempPath(), "temp.zip");
                String fullPath;
                if (names == null || names.Length == 0)
                {
                    fullPath = (contentRootPath + path);
                    byte[] bytes = System.IO.File.ReadAllBytes(fullPath);
                    FileStream fileStreamInput = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                    fileStreamResult = new FileStreamResult(fileStreamInput, "APPLICATION/octet-stream");
                }
                else if (names.Length == 1)
                {
                    fullPath = Path.Combine(contentRootPath + path, names[0]);
                    FileStream fileStreamInput = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                    fileStreamResult = new FileStreamResult(fileStreamInput, "APPLICATION/octet-stream");
                    fileStreamResult.FileDownloadName = names[0];
                }
                else if (names.Length > 1)
                {
                    string fileName = Guid.NewGuid().ToString() + "temp.zip";
                    string newFileName = fileName.Substring(36);
                    tempPath = Path.Combine(Path.GetTempPath(), newFileName);
                    if (System.IO.File.Exists(tempPath))
                    {
                        System.IO.File.Delete(tempPath);
                    }
                    string currentDirectory;
                    ZipArchiveEntry zipEntry;
                    ZipArchive archive;
                    for (int i = 0; i < names.Count(); i++)
                    {
                        fullPath = Path.Combine((contentRootPath + path), names[i]);
                        if (!string.IsNullOrEmpty(fullPath))
                        {
                            try
                            {
                                using (archive = ZipFile.Open(tempPath, ZipArchiveMode.Update))
                                {
                                    currentDirectory = Path.Combine((contentRootPath + path), names[i]);

                                    zipEntry = archive.CreateEntryFromFile(Path.Combine(this.contentRootPath, currentDirectory), names[i], CompressionLevel.Fastest);
                                }
                            }
                            catch (Exception)
                            {
                                return null;
                            }
                        }
                        else
                        {
                            throw new ArgumentNullException("name should not be null");
                        }
                    }
                    try
                    {
                        FileStream fileStreamInput = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Delete);
                        fileStreamResult = new FileStreamResult(fileStreamInput, "APPLICATION/octet-stream");
                        fileStreamResult.FileDownloadName = "files.zip";
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
                return fileStreamResult;
            }
            catch (Exception)
            {
                return null;
            }
        }

        protected FileStreamResult DownloadFolder(string path, string[] names, int count)
        {
            try
            {
                if (!String.IsNullOrEmpty(path))
                {
                    path = Path.GetDirectoryName(path);
                }
                FileStreamResult fileStreamResult;
                // create a temp.Zip file intially 
                string tempPath = Path.Combine(Path.GetTempPath(), "temp.zip");
                String fullPath;
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
                if (names.Length == 1)
                {
                    fullPath = Path.Combine(contentRootPath + path, names[0]);
                    DirectoryInfo directoryName = new DirectoryInfo(fullPath);

                    ZipFile.CreateFromDirectory(fullPath, tempPath, CompressionLevel.Fastest, true);
                    FileStream fileStreamInput = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Delete);
                    fileStreamResult = new FileStreamResult(fileStreamInput, "APPLICATION/octet-stream");
                    fileStreamResult.FileDownloadName = directoryName.Name + ".zip";
                }
                else
                {
                    string currentDirectory;
                    ZipArchiveEntry zipEntry;
                    ZipArchive archive;
                    using (archive = ZipFile.Open(tempPath, ZipArchiveMode.Update))
                    {
                        for (int i = 0; i < names.Length; i++)
                        {
                            currentDirectory = Path.Combine((contentRootPath + path), names[i]);
                            if ((File.GetAttributes(currentDirectory) & FileAttributes.Directory) == FileAttributes.Directory)
                            {
                                string[] files = Directory.GetFiles(currentDirectory, "*.*", SearchOption.AllDirectories);
                                if (files.Length == 0)
                                {
                                    zipEntry = archive.CreateEntry(names[i] + "/");
                                }
                                else
                                {
                                    foreach (string filePath in files)
                                    {
                                        zipEntry = archive.CreateEntryFromFile(filePath, names[i] + filePath.Substring(currentDirectory.Length), CompressionLevel.Fastest);
                                    }
                                }
                                foreach (string filePath in Directory.GetDirectories(currentDirectory, "*", SearchOption.AllDirectories))
                                {
                                    if (Directory.GetFiles(filePath).Length == 0)
                                    {
                                        zipEntry = archive.CreateEntry(names[i] + filePath.Substring(currentDirectory.Length) + "/");
                                    }
                                }
                            }
                            else
                            {
                                zipEntry = archive.CreateEntryFromFile(Path.Combine(this.contentRootPath, currentDirectory), names[i], CompressionLevel.Fastest);

                            }
                        }
                    }
                    FileStream fileStreamInput = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Delete);
                    fileStreamResult = new FileStreamResult(fileStreamInput, "application/force-download");
                    fileStreamResult.FileDownloadName = "folders.zip";
                }
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
                return fileStreamResult;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private string DirectoryRename(string newPath)
        {
            int directoryCount = 0;
            while (System.IO.Directory.Exists(newPath + (directoryCount > 0 ? "(" + directoryCount.ToString() + ")" : "")))
            {
                directoryCount++;
            }
            newPath = newPath + (directoryCount > 0 ? "(" + directoryCount.ToString() + ")" : "");
            return newPath;
        }

        private string FileRename(string newPath, string fileName)
        {
            int name = newPath.LastIndexOf(".");
            if (name >= 0)
            {
                newPath = newPath.Substring(0, name);
            }
            int fileCount = 0;
            while (System.IO.File.Exists(newPath + (fileCount > 0 ? "(" + fileCount.ToString() + ")" + Path.GetExtension(fileName) : Path.GetExtension(fileName))))
            {
                fileCount++;
            }
            newPath = newPath + (fileCount > 0 ? "(" + fileCount.ToString() + ")" : "") + Path.GetExtension(fileName);
            return newPath;
        }


        private string DirectoryCopy(string sourceDirName, string destDirName)
        {
            string result = String.Empty;
            try
            {
                // Gets the subdirectories for the specified directory.
                DirectoryInfo dir = new DirectoryInfo(sourceDirName);

                DirectoryInfo[] dirs = dir.GetDirectories();
                // If the destination directory doesn't exist, creates it.
                if (!Directory.Exists(destDirName))
                {
                    try
                    {
                        Directory.CreateDirectory(destDirName);
                    }
                    catch (Exception e)
                    {
                        if (e.GetType().Name == "UnauthorizedAccessException")
                        {
                            return destDirName;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                // Gets the files in the directory and copy them to the new location.
                FileInfo[] files = dir.GetFiles();
                foreach (FileInfo file in files)
                {
                    try
                    {
                        string oldPath = Path.Combine(sourceDirName, file.Name);
                        string temppath = Path.Combine(destDirName, file.Name);
                        File.Copy(oldPath, temppath);
                    }
                    catch (Exception e)
                    {
                        if (e.GetType().Name == "UnauthorizedAccessException")
                        {
                            return file.FullName;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
                foreach (DirectoryInfo direc in dirs)
                {
                    string oldPath = Path.Combine(sourceDirName, direc.Name);
                    string temppath = Path.Combine(destDirName, direc.Name);
                    result = DirectoryCopy(oldPath, temppath);
                    if (result != String.Empty)
                    {
                        return result;
                    }
                }
                return result;
            }
            catch (Exception e)
            {
                if (e.GetType().Name == "UnauthorizedAccessException")
                {
                    return sourceDirName;
                }
                else
                {
                    throw;
                }
            }
        }


        protected virtual string DeleteDirectory(string path)
        {
            try
            {
                string result = String.Empty;
                string[] files = Directory.GetFiles(path);
                string[] dirs = Directory.GetDirectories(path);
                foreach (string file in files)
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch (Exception e)
                    {
                        if (e.GetType().Name == "UnauthorizedAccessException")
                        {
                            return file;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
                foreach (string dir in dirs)
                {
                    result = DeleteDirectory(dir);
                    if (result != String.Empty)
                    {
                        return result;
                    }
                }
                Directory.Delete(path, true);
                return result;
            }
            catch (Exception e)
            {
                if (e.GetType().Name == "UnauthorizedAccessException")
                {
                    return path;
                }
                else
                {
                    throw;
                }

            }
        }
        protected virtual FileManagerDirectoryContent GetFileDetails(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                FileAttributes attr = File.GetAttributes(path);
                FileInfo detailPath = new FileInfo(info.FullName);
                int folderLength = 0;
                bool isFile = ((attr & FileAttributes.Directory) == FileAttributes.Directory) ? false : true;
                if (!isFile)
                {
                    folderLength = detailPath.Directory != null ? detailPath.Directory.GetDirectories().Length : folderLength;
                }
                string filterPath = GetRelativePath(this.contentRootPath, info.DirectoryName + Path.DirectorySeparatorChar);
                return new FileManagerDirectoryContent
                {
                    Name = info.Name,
                    Size = isFile ? info.Length : 0,
                    IsFile = isFile,
                    DateModified = info.LastWriteTime,
                    DateCreated = info.CreationTime,
                    Type = info.Extension,
                    HasChild = isFile ? false : (CheckChild(info.FullName)),
                    FilterPath = filterPath,
                    Permission = GetPermission(GetPath(filterPath), info.Name, isFile)
                };
            }
            catch (Exception)
            {
                throw;
            }
        }

        protected virtual AccessPermission GetPermission(string location, string name, bool isFile)
        {
            AccessPermission FilePermission = new AccessPermission();
            string nameExtension = string.Empty;
            string fileName = string.Empty;
            string currentPath = string.Empty;
            if (isFile)
            {
                if (this.AccessDetails.AccessRules == null) return null;
                if(name != null)
                {
                    nameExtension = Path.GetExtension(name).ToLower();
                    fileName = Path.GetFileNameWithoutExtension(name);
                    currentPath = GetFilePath(location + name);
                }

                foreach (AccessRule fileRule in AccessDetails.AccessRules)
                {
                    if (!string.IsNullOrEmpty(fileRule.Path) && fileRule.IsFile && (fileRule.Role == null || fileRule.Role == AccessDetails.Role))
                    {
                        if (fileRule.Path.IndexOf("*.*") > -1)
                        {
                            string parentPath = fileRule.Path.Substring(0, fileRule.Path.IndexOf("*.*"));
                            if (currentPath.IndexOf(GetPath(parentPath)) == 0 || parentPath == "")
                            {
                                FilePermission = UpdateFileRules(FilePermission, fileRule);
                            }
                        }
                        else if (fileRule.Path.IndexOf("*.") > -1)
                        {
                            string pathExtension = Path.GetExtension(fileRule.Path).ToLower();
                            string parentPath = fileRule.Path.Substring(0, fileRule.Path.IndexOf("*."));
                            if ((GetPath(parentPath) == currentPath || parentPath == "") && nameExtension == pathExtension)
                            {
                                FilePermission = UpdateFileRules(FilePermission, fileRule);
                            }
                        }
                        else if (fileRule.Path.IndexOf(".*") > -1)
                        {
                            string pathName = Path.GetFileNameWithoutExtension(fileRule.Path);
                            string parentPath = fileRule.Path.Substring(0, fileRule.Path.IndexOf(pathName + ".*"));
                            if ((GetPath(parentPath) == currentPath || parentPath == "") && fileName == pathName)
                            {
                                FilePermission = UpdateFileRules(FilePermission, fileRule);
                            }
                        }
                        else if (GetPath(fileRule.Path) == GetValidPath(location + name))
                        {
                            FilePermission = UpdateFileRules(FilePermission, fileRule);
                        }
                    }
                }
                return FilePermission;
            }
            else
            {
                if (this.AccessDetails.AccessRules == null) { return null; }
                foreach (AccessRule folderRule in AccessDetails.AccessRules)
                {
                    if (folderRule.Path != null && folderRule.IsFile == false && (folderRule.Role == null || folderRule.Role == AccessDetails.Role))
                    {
                        if (folderRule.Path.IndexOf("*") > -1)
                        {
                            string parentPath = folderRule.Path.Substring(0, folderRule.Path.IndexOf("*"));
                            if (GetValidPath(location + name).IndexOf(GetPath(parentPath)) == 0 || parentPath == "")
                            {
                                FilePermission = UpdateFolderRules(FilePermission, folderRule);
                            }
                        }
                        else if (GetPath(folderRule.Path) == GetValidPath(location + name) || GetPath(folderRule.Path) == GetValidPath(location + name + Path.DirectorySeparatorChar))
                        {
                            FilePermission = UpdateFolderRules(FilePermission, folderRule);
                        }
                        else if (GetValidPath(location + name).IndexOf(GetPath(folderRule.Path)) == 0)
                        {
                            FilePermission.Write = HasPermission(folderRule.WriteContents);
                            FilePermission.WriteContents = HasPermission(folderRule.WriteContents);
                        }
                    }
                }
                return FilePermission;
            }
        }

        protected virtual string GetPath(string path)
        {
            String fullPath = (this.contentRootPath + path);
            DirectoryInfo directory = new DirectoryInfo(fullPath);
            return directory.FullName;
        }
        protected virtual string GetValidPath(string path)
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            return directory.FullName;
        }
        protected virtual string GetFilePath(string path)
        {
            return Path.GetDirectoryName(path) + Path.DirectorySeparatorChar;
        }
        protected virtual string[] GetFolderDetails(string path)
        {
            string[] str_array = path.Split('/'), fileDetails = new string[2];
            string parentPath = "";
            for (int i = 0; i < str_array.Length - 2; i++)
            {
                parentPath += str_array[i] + "/";
            }
            fileDetails[0] = parentPath;
            fileDetails[1] = str_array[str_array.Length - 2];
            return fileDetails;
        }

        protected virtual AccessPermission GetPathPermission(string path)
        {
            path = path != null ? path : string.Empty;
            string[] fileDetails = GetFolderDetails(path);
            return GetPermission(GetPath(fileDetails[0]), fileDetails[1], false);
        }

        protected virtual AccessPermission GetFilePermission(string path)
        {
            string parentPath = path.Substring(0, path.LastIndexOf("/") + 1);
            string fileName = Path.GetFileName(path);
            return GetPermission(GetPath(parentPath), fileName, true);
        }
        protected virtual bool IsDirectory(string path, string fileName)
        {
            String fullPath = Path.Combine(path, fileName);
            return ((File.GetAttributes(fullPath) & FileAttributes.Directory) != FileAttributes.Directory) ? false : true;
        }
        protected virtual bool HasPermission(Permission rule)
        {
            return rule == Permission.Allow ? true : false;
        }
        protected virtual AccessPermission UpdateFileRules(AccessPermission filePermission, AccessRule fileRule)
        {
            filePermission.Copy = HasPermission(fileRule.Copy);
            filePermission.Download = HasPermission(fileRule.Download);
            filePermission.Write = HasPermission(fileRule.Write);
            filePermission.Read = HasPermission(fileRule.Read);
            filePermission.Message = string.IsNullOrEmpty(fileRule.Message) ? string.Empty : fileRule.Message;
            return filePermission;
        }
        protected virtual AccessPermission UpdateFolderRules(AccessPermission folderPermission, AccessRule folderRule)
        {
            folderPermission.Copy = HasPermission(folderRule.Copy);
            folderPermission.Download = HasPermission(folderRule.Download);
            folderPermission.Write = HasPermission(folderRule.Write);
            folderPermission.WriteContents = HasPermission(folderRule.WriteContents);
            folderPermission.Read = HasPermission(folderRule.Read);
            folderPermission.Upload = HasPermission(folderRule.Upload);
            folderPermission.Message = string.IsNullOrEmpty(folderRule.Message) ? string.Empty : folderRule.Message;
            return folderPermission;
        }
        protected virtual bool parentsHavePermission(FileManagerDirectoryContent fileDetails)
        {
            String parentPath = fileDetails.FilterPath.Replace(Path.DirectorySeparatorChar, '/');
            String[] parents = parentPath.Split('/');
            String currPath = "/";
            bool hasPermission = true;
            for (int i = 0; i <= parents.Length - 2; i++)
            {
                currPath = (parents[i] == "") ? currPath : (currPath + parents[i] + "/");

                AccessPermission PathPermission = GetPathPermission(currPath);

                if (PathPermission == null)
                {
                    break;
                }
                else if (PathPermission != null && !PathPermission.Read)
                {
                    hasPermission = false;
                    break;
                }
            }
            return hasPermission;
        }
        public string ToCamelCase(FileManagerResponse userData)
        {
            return JsonConvert.SerializeObject(userData, new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                }
            });
        }

        FileStreamResult FileProviderBase.Download(string path, string[] names, params FileManagerDirectoryContent[] data)
        {
            throw new NotImplementedException();
        }

        private bool CheckChild(string path)
        {
            bool hasChild;
            try
            {
                DirectoryInfo directory = new DirectoryInfo(path);
                DirectoryInfo[] dir = directory.GetDirectories();
                hasChild = dir.Length != 0;
            }
            catch (Exception e)
            {
                if (e.GetType().Name == "UnauthorizedAccessException")
                {
                    hasChild = false;
                }
                else
                {
                    throw;
                }
            }
            return hasChild;
        }
        private bool hasAccess(string path)
        {
            bool hasAcceess;
            try
            {
                DirectoryInfo directory = new DirectoryInfo(path);
                DirectoryInfo[] dir = directory.GetDirectories();
                hasAcceess = dir != null;
            }
            catch (Exception e)
            {
                if (e.GetType().Name == "UnauthorizedAccessException")
                {
                    hasAcceess = false;
                }
                else
                {
                    throw;
                }
            }
            return hasAcceess;
        }
        private long GetDirectorySize(DirectoryInfo dir, long size)
        {
            try
            {
                foreach (DirectoryInfo subdir in dir.GetDirectories())
                {
                    size = GetDirectorySize(subdir, size);
                }
                foreach (FileInfo file in dir.GetFiles())
                {
                    size += file.Length;
                }
            }
            catch (Exception e)
            {
                if (e.GetType().Name != "UnauthorizedAccessException")
                {
                    throw;
                }
            }
            return size;
        }
        private List<FileInfo> GetDirectoryFiles(DirectoryInfo dir, List<FileInfo> files)
        {
            try
            {
                foreach (DirectoryInfo subdir in dir.GetDirectories())
                {
                    files = GetDirectoryFiles(subdir, files);
                }
                foreach (FileInfo file in dir.GetFiles())
                {
                    files.Add(file);
                }
            }
            catch (Exception e)
            {
                if (e.GetType().Name != "UnauthorizedAccessException")
                {
                    throw;
                }
            }
            return files;
        }
        private List<DirectoryInfo> GetDirectoryFolders(DirectoryInfo dir, List<DirectoryInfo> files)
        {
            try
            {
                foreach (DirectoryInfo subdir in dir.GetDirectories())
                {
                    files = GetDirectoryFolders(subdir, files);
                }
                foreach (DirectoryInfo file in dir.GetDirectories())
                {
                    files.Add(file);
                }
            }
            catch (Exception e)
            {
                if (e.GetType().Name != "UnauthorizedAccessException")
                {
                    throw;
                }
            }
            return files;
        }
        private string getFileNameFromPath(string path)
        {
            int index = path.LastIndexOf("/");
            return path.Substring(index + 1);
        }

    }
}
