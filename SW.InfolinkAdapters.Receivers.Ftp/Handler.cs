﻿
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rebex.Net;
using SW.PrimitiveTypes;
using SW.Serverless.Sdk;

namespace SW.InfolinkAdapters.Receivers.Ftp
{
    class Handler : IInfolinkReceiver
    {
        private IFtp _ftpOrSftp;
        private string _targetPath = string.Empty;

        public Handler()
        {
            Runner.Expect(CommonProperties.LicenseKey);
            Runner.Expect(CommonProperties.Host);
            Runner.Expect(CommonProperties.Port, null);
            Runner.Expect(CommonProperties.Username);
            Runner.Expect(CommonProperties.Password);
            Runner.Expect(CommonProperties.TargetPath, "");
            Runner.Expect(CommonProperties.BatchSize, "50");
            Runner.Expect(CommonProperties.ResponseEncoding, "utf8");
            Runner.Expect(CommonProperties.DeleteMovesFileTo, null);
            Runner.Expect(CommonProperties.Protocol, "sftp");
        }

        public async Task DeleteFile(string fileId)
        {
            if (!await _ftpOrSftp.FileExistsAsync(_targetPath + "/" + fileId)) return;
            var deleteMovesFileTo = Runner.StartupValueOf("DeleteMovesFileTo");
            if (string.IsNullOrWhiteSpace(deleteMovesFileTo))
            {
                await _ftpOrSftp.DeleteFileAsync(_targetPath + "/" + fileId);
            }
            else
            {
                await _ftpOrSftp.RenameAsync(_targetPath + "/" + fileId,
                    deleteMovesFileTo + "/" + fileId);
            }

        }

        public async Task Finalize()
        {
            await _ftpOrSftp.DisconnectAsync();
            _ftpOrSftp.Dispose();
        }

        public async Task<XchangeFile> GetFile(string fileId)
        {

            // download a remote file into a memory stream
            await using var stream = new MemoryStream();
            await _ftpOrSftp.GetFileAsync(_targetPath + "/" + fileId, stream);

            // convert memory stream to data
            var data = stream.ToArray();

            return (Runner.StartupValueOf(CommonProperties.ResponseEncoding).ToLower()) switch
            {
                "base64" => new XchangeFile(Convert.ToBase64String(data), fileId),
                "utf8" => new XchangeFile(Encoding.UTF8.GetString(data), fileId),
                _ => throw new ArgumentException($"Unknown {nameof(CommonProperties.ResponseEncoding)} '{Runner.StartupValueOf(CommonProperties.ResponseEncoding)}'"),
            };
        }

        public async Task Initialize()
        {

            Rebex.Licensing.Key = Runner.StartupValueOf(CommonProperties.LicenseKey);

            switch (Runner.StartupValueOf(CommonProperties.Protocol).ToLower())
            {
                case "sftp":

                    var sftp = new Sftp();
                    int sftpPort = string.IsNullOrWhiteSpace(Runner.StartupValueOf(CommonProperties.Port)) ? 22 : Convert.ToInt32(Runner.StartupValueOf(CommonProperties.Port));
                    await sftp.ConnectAsync(Runner.StartupValueOf(CommonProperties.Host), sftpPort);
                    _ftpOrSftp = sftp;
                    break;

                case "ftp":

                    var ftp = new Rebex.Net.Ftp();
                    int ftpPort = string.IsNullOrWhiteSpace(Runner.StartupValueOf(CommonProperties.Port)) ? 21 : Convert.ToInt32(Runner.StartupValueOf(CommonProperties.Port));
                    await ftp.ConnectAsync(Runner.StartupValueOf(CommonProperties.Host), ftpPort);
                    _ftpOrSftp = ftp;
                    break;

                default:
                    throw new ArgumentException($"Unknown protocol '{Runner.StartupValueOf(CommonProperties.Protocol)}'");

            }

            // authenticate
            await _ftpOrSftp.LoginAsync(Runner.StartupValueOf(CommonProperties.Username), Runner.StartupValueOf(CommonProperties.Password));

            _targetPath = Runner.StartupValueOf(CommonProperties.TargetPath);
        }

        public async Task<IEnumerable<string>> ListFiles()
        {
            var fileNames = await _ftpOrSftp.GetNameListAsync(_targetPath);
            var batchSize = Convert.ToInt32(Runner.StartupValueOf(CommonProperties.BatchSize));
            return fileNames.Take(batchSize).ToArray();
        }
    }
}
