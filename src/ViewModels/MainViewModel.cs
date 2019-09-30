﻿using Ravenfall.Updater.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Ravenfall.Updater.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IGameUpdater updater;

        #region Property Fields
        private string oldVersionName;
        private string newVersionName;
        private float updateProgress;
        private string message;
        #endregion

        public MainViewModel(IGameUpdater updater)
        {
            this.updater = updater;
            this.updater.StatusChanged += Updater_StatusChanged;
            this.updater.UpdateCompleted += Updater_UpdateCompleted;
            this.updater.Start();
        }


        private void Updater_UpdateCompleted(object sender, GameUpdateChangedEventArgs e)
        {
            UpdateView(e);
        }

        private void Updater_StatusChanged(object sender, GameUpdateChangedEventArgs e)
        {
            UpdateView(e);
            // shutdown
            // restart, whatnot
        }

        internal void Start()
        {
            this.updater.Start();
        }

        private void UpdateView(GameUpdateChangedEventArgs e)
        {
            this.Message = e.Message;
            this.UpdateProgress = e.Progress;
            this.OldVersionName = e.OldVersion;
            this.NewVersionName = e.NewVersion;
        }

        #region Properties

        public string Message
        {
            get => message;
            set => Set(ref message, value);
        }

        public float UpdateProgress
        {
            get => updateProgress;
            set => Set(ref updateProgress, value);
        }

        public string NewVersionName
        {
            get => newVersionName;
            set => Set(ref newVersionName, value);
        }

        public string OldVersionName
        {
            get => oldVersionName;
            set => Set(ref oldVersionName, value);
        }

        #endregion

        public static MainViewModel DesignInstance => new MainViewModel(null)
        {
            Message = "Test",
            UpdateProgress = 46f,
            NewVersionName = "1.0f",
            OldVersionName = "0.9b"
        };
    }
}
