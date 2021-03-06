﻿using System;
using System.IO;
using EmaXamarin.Api;
using EmaXamarin.CloudStorage;
using EmaXamarin.CloudStorage.Dropbox;
using Xamarin.Forms;

namespace EmaXamarin.Pages
{
    public class SettingsPage : ContentPage
    {
        private readonly IFileRepository _fileRepository;
        private readonly ApplicationEvents _applicationEvents;
        private SwitchCell _customStorageSwitch;
        private EntryCell _customStorageDirectoryEntry;
        private DropboxUserPermission _dropboxUserPermission;
        private SwitchCell _dropboxSwitch;

        public SettingsPage(IFileRepository fileRepository, IExternalBrowserService externalBrowserService, ApplicationEvents applicationEvents)
        {
            Title = "Settings";
            _fileRepository = fileRepository;
            _applicationEvents = applicationEvents;

            InitializeStorageSettings(fileRepository);
            InitializeDropboxSettings(externalBrowserService, applicationEvents);

            var autoSyncValue = new EntryCell
            {
                Label = "Interval in minutes",
                IsEnabled = PersistedState.SyncInterval > 0,
                Text = PersistedState.SyncInterval > 0 ? PersistedState.SyncInterval.ToString() : string.Empty
            };
            var autoSyncSwitch = new SwitchCell
            {
                Text = "Auto sync",
                On = PersistedState.SyncInterval > 0
            };
            autoSyncSwitch.OnChanged += (sender, args) =>
            {
                if (args.Value)
                {
                    autoSyncValue.Text = "10";
                    autoSyncValue.IsEnabled = true;
                    SetSyncInterval(10);
                }
                else
                {
                    autoSyncValue.Text = string.Empty;
                    autoSyncValue.IsEnabled = false;
                    SetSyncInterval(0);
                }
            };
            autoSyncValue.Completed += (sender, args) =>
            {
                int value;
                SetSyncInterval(int.TryParse(autoSyncValue.Text, out value) ? value : 0);
            };

            Content = new TableView
            {
                Intent = TableIntent.Settings,
                Root = new TableRoot
                {
                    new TableSection("Storage")
                    {
                        _customStorageSwitch,
                        _customStorageDirectoryEntry
                    },
                    new TableSection("Cloud sync")
                    {
                        autoSyncSwitch,
                        autoSyncValue,
                        _dropboxSwitch
                    }
                }
            };
        }

        private void SetSyncInterval(int interval)
        {
            PersistedState.SyncInterval = interval;
            SyncBootstrapper.RefreshFromSyncInterval();
        }

        private void InitializeDropboxSettings(IExternalBrowserService externalBrowserService, ApplicationEvents applicationEvents)
        {
            _dropboxSwitch = new SwitchCell
            {
                Text = "Use Dropbox",
                On = PersistedState.UserLogin != null && !string.IsNullOrEmpty(PersistedState.UserLogin.Secret)
            };
            _dropboxSwitch.OnChanged += (sender, args) =>
            {
                if (args.Value)
                {
                    applicationEvents.Resumed += ApplicationEventsOnResumed;
                    _dropboxUserPermission = new DropboxUserPermission();
                    _dropboxUserPermission.AskUserForPermission(externalBrowserService);
                    //(continue in ApplicationEventsOnResumed())
                }
                else
                {
                    PersistedState.UserLogin = null;
                }
                SyncBootstrapper.RefreshDropboxSync(_fileRepository);
            };
        }

        private async void ApplicationEventsOnResumed(object sender, EventArgs eventArgs)
        {
            _applicationEvents.Resumed -= ApplicationEventsOnResumed;
            if (_dropboxUserPermission != null)
            {
                var userPermission = await _dropboxUserPermission.VerifiedUserPermission();
                if (string.IsNullOrEmpty(userPermission.Token))
                {
                    _dropboxSwitch.On = false;
                    await DisplayAlert("Not good", "Dropbox did not return a valid token.", "OK");
                }
                else
                {
                    PersistedState.UserLogin = userPermission;
                }
            }
            SyncBootstrapper.RefreshDropboxSync(_fileRepository);
        }

        private void InitializeStorageSettings(IFileRepository fileRepository)
        {
            bool hasCustomStorageDir = fileRepository.StorageDirectory != fileRepository.DefaultStorageDirectory;
            _customStorageSwitch = new SwitchCell
            {
                Text = "Custom storage directory",
                On = hasCustomStorageDir
            };
            _customStorageDirectoryEntry = new EntryCell
            {
                Label = "Path",
                Text = fileRepository.StorageDirectory,
                IsEnabled = hasCustomStorageDir
            };
            _customStorageSwitch.OnChanged += (sender, args) =>
            {
                _customStorageDirectoryEntry.IsEnabled = args.Value;
                if (args.Value)
                {
                    //reset to default
                    _customStorageDirectoryEntry.Text = fileRepository.DefaultStorageDirectory;
                    SetStorageDir(fileRepository.DefaultStorageDirectory);
                }
            };
            _customStorageDirectoryEntry.Completed += (sender, args) => { SetStorageDir(_customStorageDirectoryEntry.Text); };
        }

        /// <summary>
        /// set the storage dir to a different value (or null for the default value)
        /// </summary>
        /// <param name="value"></param>
        private async void SetStorageDir(string value)
        {
            Exception exception = null;
            try
            {
                if (_fileRepository.StorageDirectory != value)
                {
                    string answer = await DisplayActionSheet("Wiki storage directory changed", "Cancel", null, "Move data to new directory", "Copy data to new directory", "Leave data alone");

                    switch (answer)
                    {
                        case "Cancel":
                            _customStorageDirectoryEntry.Text = _fileRepository.StorageDirectory;
                            return;

                        case "Move data to new directory":
                            await _fileRepository.MoveTo(value);
                            break;

                        case "Copy data to new directory":
                            await _fileRepository.CopyTo(value);
                            break;

                        default:
                            _fileRepository.StorageDirectory = value;
                            break;
                    }

                    if (_fileRepository.DefaultStorageDirectory == value)
                    {
                        value = null;
                        _customStorageSwitch.On = false;
                    }
                    PersistedState.CustomStorageDirectory = value;
                }
            }
            catch (IOException ex)
            {
                exception = ex;
            }

            if (exception != null)
            {
                //await is not allowed in the catch clause (by Xamarin studio)
                await DisplayAlert("Not good", exception.Message, "OK");
            }
        }
    }
}