﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using SharePointOnlineTasker.Entities;
using SharePointOnlineTasker.Interfaces;
using SharePointOnlineTasker.Interfaces.DriveTasks;

namespace SharePointOnlineTasker.Services
{
    public class TasksRunner : ITasksRunner
    {
        private readonly ILogger<TasksRunner> _logger;
        private readonly IAuthenticationProvider _authenticator;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly IDriveFileTask _googleDriveFileTask;

        public TasksRunner(ILogger<TasksRunner> logger, IAuthenticationProvider authenticator, IConfiguration configuration, IMemoryCache cache, IDriveFileTask googleDriveFileTask)
        {
            _logger = logger;
            _authenticator = authenticator;
            _configuration = configuration;
            _cache = cache;
            _googleDriveFileTask = googleDriveFileTask;
        }

        public async Task ExecuteAsync()
        {
            GraphServiceClient graphClient = new GraphServiceClient(_authenticator);

            Group group = await GetGroupAsync(graphClient);
            if (group != null)
            {
                Drive drive = await GetDriveAsync(graphClient, group);
                if (drive != null)
                {
                    var collectionPage = await graphClient
                        .Drives[drive.Id]
                        .Root
                        .Children
                        .Request()
                        .GetAsync();
                    
                    do
                    {
                        foreach (DriveItem item in collectionPage)
                        {
                            await RunAsync(graphClient, drive, item);
                        }

                        if (collectionPage.NextPageRequest != null)
                        {
                            collectionPage = await collectionPage.NextPageRequest.GetAsync();
                        }
                    } while (collectionPage?.NextPageRequest != null);
                }
            }
        }

        private async Task RunAsync(GraphServiceClient graphClient, Drive drive, DriveItem item)
        {
            if (item.File != null)
            {
                DriveFile driveFile = new DriveFile(item.Id, item.Name, item.WebUrl.Substring(item.WebUrl.IndexOf(drive.Name)), item.File.Hashes.QuickXorHash, item.Size ?? 0);
                await _googleDriveFileTask.ExecuteAsync(driveFile);
            }
            else if (item.Folder != null)
            {
                var collectionPage = await graphClient
                    .Drives[drive.Id]
                    .Items[item.Id]
                    .Children
                    .Request()
                    .GetAsync();

                do
                {
                    foreach (DriveItem driveItem in collectionPage)
                    {
                        await RunAsync(graphClient, drive, driveItem);
                    }

                    if (collectionPage.NextPageRequest != null)
                    {
                        collectionPage = await collectionPage.NextPageRequest.GetAsync();
                    }
                } while (collectionPage?.NextPageRequest != null);
            }
        }

        private async Task<Group> GetGroupAsync(GraphServiceClient graphClient)
        {
            IGraphServiceGroupsCollectionPage groups = await graphClient.Groups.Request()
                .Filter($"startswith(displayName,'{_configuration["GroupName"]}')")
                .GetAsync();
            Group group;

            do
            {
                group = groups.FirstOrDefault(g =>
                    g.DisplayName.Equals(_configuration["GroupName"], StringComparison.InvariantCultureIgnoreCase));
                if (groups.NextPageRequest != null)
                {
                    groups = await groups.NextPageRequest
                        .Filter($"startswith(displayName,'{_configuration["GroupName"]}')")
                        .GetAsync();
                }
            } while (group == null && groups?.NextPageRequest != null);

            return group;
        }

        private async Task<Drive> GetDriveAsync(GraphServiceClient graphClient, Group group)
        {
            IGroupDrivesCollectionPage drives = await graphClient.Groups[group.Id].Drives
                .Request()
                .GetAsync();
            Drive drive;

            do
            {
                drive = drives.FirstOrDefault(d =>
                    d.Name.Equals(_configuration["DriveName"], StringComparison.InvariantCultureIgnoreCase));
                if (drives.NextPageRequest != null)
                {
                    drives = await drives.NextPageRequest
                       .GetAsync();
                }
            } while (drive == null && drives?.NextPageRequest != null);

            return drive;
        }
    }
}